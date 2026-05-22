using HarmonyLib;
using AmongUs.GameOptions;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;

namespace MalumMenu;

/// <summary>
/// HostRoleSwapManager — intercepts and rewrites role assignments at game start.
///
/// WHY BUFFER THE ENTIRE LOBBY:
/// Among Us naively sends RpcSetRole immediately for each player as the host iterates
/// through the role assignment list. There is no "batch" concept — each RPC fires as
/// soon as the host decides a player's role. If we only intercepted our own assignment,
/// we'd have no way to know who got what role, making a clean swap impossible.
///
/// By buffering ALL RpcSetRole calls until every player has been assigned (or a timeout
/// fires), we can see the complete lobby state and perform a proper 1:1 swap instead of
/// just force-overwriting our own role (which would leave the lobby in an inconsistent
/// state — two players with the same role, or a role that "vanished").
///
/// FUTURE-PROOFING:
/// This buffering approach is resilient to Among Us updates that change role assignment
/// order, add new roles, or modify the RPC flow. As long as RpcSetRole is called once
/// per player at game start, the swap logic works regardless of underlying changes.
/// </summary>

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
public static class HostRoleSwapManager
{
    // ---- Logging ----
    private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("HostRoleSwap");

    // ---- State Machine ----
    private enum SwapState
    {
        Inactive,   // Not armed, not buffering — idle
        Buffering,  // Collecting RpcSetRole calls, blocking originals
        Releasing,  // Sending swapped assignments
        Done        // Batch complete, passing through all subsequent calls
    }

    private static SwapState _state = SwapState.Inactive;

    // ---- Buffers ----
    private static readonly Dictionary<byte, RoleTypes> _bufferedAssignments = new();
    private static readonly Dictionary<byte, bool> _bufferedOverrideFlags = new();
    private static readonly HashSet<byte> _seenPlayers = new();

    // ---- Timing ----
    private static float _bufferStartTime;
    private const float BUFFER_TIMEOUT_SEC = 3f;

    // ---- Post-swap verification ----
    private static RoleTypes _expectedLocalRole;
    private static bool _pendingVerification;

    // ========================================================================
    // HARMONY PREFIX — intercepts every RpcSetRole call
    // ========================================================================

    private static RoleTypes GetTargetRole() => CheatToggles.roleSwapTarget ?? RoleTypes.Crewmate;

    public static bool Prefix(PlayerControl __instance, ref RoleTypes roleType, bool canOverrideRole)
    {
        // Fast-path: not host or feature disabled — pass through
        if (!Utils.isHost || !CheatToggles.roleSwap)
            return true;

        // Already done or currently releasing this batch — pass through
        if (_state == SwapState.Done || _state == SwapState.Releasing)
            return true;

        var localPlayer = PlayerControl.LocalPlayer;
        if (localPlayer == null)
            return true;

        var targetRole = GetTargetRole();

        // ---- State: Inactive → Buffering (first call in batch) ----
        if (_state == SwapState.Inactive)
        {
            _state = SwapState.Buffering;
            _bufferStartTime = Time.time;

            Log.LogInfo($"[HostRoleSwap] Buffering started | target={targetRole} | timeout={BUFFER_TIMEOUT_SEC}s");
        }

        // ---- Edge case: we naturally got the target role — abort swap, release cleanly ----
        if (__instance == localPlayer && roleType == targetRole)
        {
            Log.LogInfo("[HostRoleSwap] Local player already has target role — releasing without swap");
            _state = SwapState.Done;
            if (_bufferedAssignments.Count > 0)
                ReleaseOriginalAssignments();
            return true;
        }

        // ---- Buffer this assignment ----
        _seenPlayers.Add(__instance.PlayerId);
        _bufferedAssignments[__instance.PlayerId] = roleType;
        _bufferedOverrideFlags[__instance.PlayerId] = canOverrideRole;

        Log.LogDebug($"[HostRoleSwap] Buffered player {__instance.PlayerId} → {roleType} | seen={_seenPlayers.Count}/{PlayerControl.AllPlayerControls.Count}");

        // ---- Check release conditions ----
        bool allPlayersSeen = _seenPlayers.Count >= PlayerControl.AllPlayerControls.Count;
        bool timedOut = Time.time - _bufferStartTime > BUFFER_TIMEOUT_SEC;

        if (allPlayersSeen || timedOut)
        {
            if (timedOut && !allPlayersSeen)
                Log.LogWarning($"[HostRoleSwap] Timeout reached ({BUFFER_TIMEOUT_SEC}s) — releasing with {_seenPlayers.Count}/{PlayerControl.AllPlayerControls.Count} players seen");

            ReleaseBufferedAssignments(localPlayer, targetRole);
        }

        return false; // Block original — we'll send the (potentially swapped) version
    }

    // ========================================================================
    // SWAP LOGIC
    // ========================================================================

    /// <summary>
    /// Calculates and executes the role swap after all assignments have been buffered.
    ///
    /// SWAP PRIORITY (best → acceptable):
    /// 1. EXACT MATCH: Somebody already has the exact role we want → perfect 1:1 swap
    /// 2. LEGIT FALLBACK: Any same-team player (looks natural, we get their random role)
    /// 3. NORMAL FALLBACK: Force upgrade to exact role, swap target gets our original
    /// </summary>
    private static void ReleaseBufferedAssignments(PlayerControl localPlayer, RoleTypes targetRole)
    {
        _state = SwapState.Releasing;

        // Safety: if local player wasn't buffered, something went wrong — release as-is
        if (!_bufferedAssignments.TryGetValue(localPlayer.PlayerId, out var localOriginalRole))
        {
            Log.LogError("[HostRoleSwap] Local player not found in buffer — releasing original assignments");
            ReleaseOriginalAssignments();
            return;
        }

        // PRIORITY 1: Look for EXACT MATCH (somebody already has the role we want)
        var exactMatch = _bufferedAssignments
            .FirstOrDefault(a => a.Key != localPlayer.PlayerId && a.Value == targetRole);
        byte swapPartnerId = exactMatch.Key != 0 ? exactMatch.Key : byte.MaxValue;

        if (localOriginalRole == targetRole)
        {
            // Edge case: we already have the target role naturally
            Log.LogInfo("[HostRoleSwap] Local player already has target role — no swap needed");
        }
        else if (swapPartnerId != byte.MaxValue)
        {
            // PRIORITY 1: Perfect 1:1 swap with exact match
            _bufferedAssignments[localPlayer.PlayerId] = targetRole;
            _bufferedAssignments[swapPartnerId] = localOriginalRole;
            _expectedLocalRole = targetRole;
            Log.LogInfo($"[HostRoleSwap] EXACT MATCH swap: local({localOriginalRole}→{targetRole}) ↔ player{swapPartnerId}({targetRole}→{localOriginalRole})");
        }
        else if (CheatToggles.roleSwapLegit)
        {
            // PRIORITY 2: LEGIT MODE — swap with any same-team player
            byte teamSwapTargetId = FindPlayerOnSameTeam(targetRole, localPlayer.PlayerId);
            if (teamSwapTargetId != byte.MaxValue)
            {
                var theirRole = _bufferedAssignments[teamSwapTargetId];
                _bufferedAssignments[localPlayer.PlayerId] = theirRole;
                _bufferedAssignments[teamSwapTargetId] = localOriginalRole;
                _expectedLocalRole = theirRole;
                Log.LogInfo($"[HostRoleSwap] LEGIT swap: local({localOriginalRole}→{theirRole}) ↔ player{teamSwapTargetId}({theirRole}→{localOriginalRole})");
            }
            else
            {
                Log.LogWarning("[HostRoleSwap] LEGIT mode: no same-team player found — releasing unchanged");
            }
        }
        else
        {
            // PRIORITY 3: NORMAL MODE — force upgrade to exact role
            byte teamSwapTargetId = FindPlayerOnSameTeam(targetRole, localPlayer.PlayerId);
            if (teamSwapTargetId != byte.MaxValue)
            {
                var teamRole = _bufferedAssignments[teamSwapTargetId];
                _bufferedAssignments[localPlayer.PlayerId] = teamRole;
                _bufferedAssignments[teamSwapTargetId] = localOriginalRole;
                Log.LogInfo($"[HostRoleSwap] NORMAL team-swap: local({localOriginalRole}→{teamRole}) ↔ player{teamSwapTargetId}({teamRole}→{localOriginalRole})");
            }
            // Force upgrade to exact target role
            _bufferedAssignments[localPlayer.PlayerId] = targetRole;
            _expectedLocalRole = targetRole;
            Log.LogInfo($"[HostRoleSwap] NORMAL force-upgrade: local → {targetRole}");
        }

        _pendingVerification = true;
        ReleaseOriginalAssignments();
    }

    // ========================================================================
    // RPC SENDING
    // ========================================================================

    /// <summary>
    /// Sends the actual RpcSetRole call with the preserved canOverrideRole flag.
    /// Uses the stored flag from _bufferedOverrideFlags to ensure the RPC is identical
    /// to what the game originally sent (prevents role assignment bugs).
    /// </summary>
    private static void ForceSetRoleNetworked(PlayerControl player, RoleTypes role)
    {
        _bufferedOverrideFlags.TryGetValue(player.PlayerId, out var canOverrideRole);
        player.RpcSetRole(role, canOverrideRole);
    }

    /// <summary>
    /// Sends all buffered RpcSetRole calls with potentially modified (swapped) roles.
    /// Clears all buffers when complete.
    /// </summary>
    private static void ReleaseOriginalAssignments()
    {
        foreach (var assignment in _bufferedAssignments)
        {
            foreach (var player in PlayerControl.AllPlayerControls)
            {
                if (player.PlayerId != assignment.Key) continue;

                try
                {
                    ForceSetRoleNetworked(player, assignment.Value);
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[HostRoleSwap] Failed to release assignment for player {assignment.Key}: {ex.Message}");
                }

                break;
            }
        }

        _bufferedAssignments.Clear();
        _bufferedOverrideFlags.Clear();
        _seenPlayers.Clear();
        _state = SwapState.Done;

        Log.LogInfo("[HostRoleSwap] All assignments released — batch complete");
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// <summary>
    /// Finds a player on the same team as targetRole, excluding the specified player.
    /// Used as fallback when no exact role match is available.
    /// </summary>
    /// <returns>PlayerId of found player, or byte.MaxValue if none found</returns>
    private static byte FindPlayerOnSameTeam(RoleTypes targetRole, byte excludedPlayerId)
    {
        var match = _bufferedAssignments
            .FirstOrDefault(a => a.Key != excludedPlayerId && IsSameTeam(a.Value, targetRole));
        return match.Key != 0 ? match.Key : byte.MaxValue;
    }

    private static bool IsSameTeam(RoleTypes role, RoleTypes targetRole)
    {
        return IsImpostorRole(role) == IsImpostorRole(targetRole);
    }

    private static bool IsImpostorRole(RoleTypes role)
    {
        return role == RoleTypes.Impostor
            || role == RoleTypes.Shapeshifter
            || role == RoleTypes.Phantom
            || role == RoleTypes.Viper;
    }

    // ========================================================================
    // STATE MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Aggressively resets all state. Call on: game start, game end, host change,
    /// lobby leave, scene load — anywhere stale state could cause issues.
    /// </summary>
    public static void ResetState()
    {
        if (_state == SwapState.Inactive && _bufferedAssignments.Count == 0)
            return; // Already clean

        Log.LogInfo($"[HostRoleSwap] ResetState called | was={_state} | buffered={_bufferedAssignments.Count}");

        _bufferedAssignments.Clear();
        _bufferedOverrideFlags.Clear();
        _seenPlayers.Clear();
        _state = SwapState.Inactive;
        _bufferStartTime = 0f;
        _expectedLocalRole = default;
        _pendingVerification = false;
    }

    /// <summary>
    /// Post-swap verification: checks if the local player actually received the
    /// expected role after the swap. Call this after a short delay (e.g. in Update
    /// or via a coroutine) once roles should be settled.
    /// </summary>
    public static void VerifySwap()
    {
        if (!_pendingVerification)
            return;

        _pendingVerification = false;

        var localPlayer = PlayerControl.LocalPlayer;
        if (localPlayer == null || localPlayer.Data == null)
        {
            Log.LogWarning("[HostRoleSwap] Verification skipped — local player not available");
            return;
        }

        var actualRole = localPlayer.Data.RoleType;
        if (actualRole == _expectedLocalRole)
        {
            Log.LogInfo($"[HostRoleSwap] VERIFIED: local role is {actualRole} (expected {_expectedLocalRole})");
        }
        else
        {
            Log.LogError($"[HostRoleSwap] MISMATCH: local role is {actualRole} but expected {_expectedLocalRole} — swap may have failed!");
        }
    }
}
