using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MalumMenu;

public sealed class Radar : MonoBehaviour
{
    public static bool lockPosition = false;
    public static float scale = 0.35f;
    public static float scaleOffsetPercent;
    public static float baseScale = 0.35f;
    public static Vector2 anchoredPosition = new Vector2(float.MinValue, float.MinValue);

    public static RectTransform GetWindow()
    {
        var instance = FindObjectOfType<Radar>();
        return instance?._window;
    }

    private static Texture2D LoadEmbeddedTexture(string textureName)
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = $"MalumMenu.Resources.Maps.{textureName}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                MalumMenu.Log?.LogError($"LoadEmbeddedTexture: resource not found '{resourceName}'");
                return null;
            }

            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);

            var tex = new Texture2D(2, 2);
            tex.LoadImage(buffer);
            return tex;
        }
        catch (Exception ex)
        {
            MalumMenu.Log?.LogError($"LoadEmbeddedTexture: Exception: {ex.Message}");
            return null;
        }
    }

    private static Sprite CreateSpriteFromTexture(Texture2D tex)
    {
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    private static Sprite _fallbackIcon;

    private static Sprite GetFallbackIcon()
    {
        if (_fallbackIcon != null) return _fallbackIcon;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        var center = size / 2f;
        var radius = size / 2f - 1;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = dist <= radius ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _fallbackIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _fallbackIcon;
    }

    private static Texture2D LoadEmbeddedCrewmateIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = "MalumMenu.Resources.radar_crewmate_icon.png";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);

            var tex = new Texture2D(2, 2);
            tex.LoadImage(buffer);
            return tex;
        }
        catch (Exception ex)
        {
            MalumMenu.Log?.LogError($"LoadEmbeddedCrewmateIcon: Exception: {ex.Message}");
            return null;
        }
    }

    public const float MinScale = 0.1f;
    public const float MaxScale = 3.0f;
    private const float BaseSizeAtDefaultScale = 300f;
    public const float DefaultScale = 0.35f;
    private const float IconSize = 14f;
    private const float HighlightSize = 18f;
    private const float BodySize = 16f;
    private const float HaloSizeMult = 2.0f;
    private const float TrailWidth = 2f;
    private const int TrailMaxWaypoints = 256;
    private const float TrailWaypointIntervalSeconds = 0.1f;
    private const int TrailMaxSegments = 128;
    private const float TrailAlphaStart = 1f;
    private const float TrailAlphaEnd = 0f;
    private const float TrailFadeStart = 0.75f;
    private const int MeetingFreezeStepsBack = 1;

    private static Sprite _fallbackSprite;
    private static Sprite _radialSprite;

    private readonly Dictionary<byte, RadarPlayerUi> _uiByPlayer = new Dictionary<byte, RadarPlayerUi>(16);
    private readonly List<byte> _tmpRemove = new List<byte>(16);
    private readonly Dictionary<byte, Vector2> _playerMapLocalById = new Dictionary<byte, Vector2>(16);
    private readonly Dictionary<byte, Vector2> _frozenPlayerMapLocalById = new Dictionary<byte, Vector2>(16);
    private readonly Dictionary<int, Image> _bodyUiById = new Dictionary<int, Image>(16);
    private readonly List<int> _tmpRemoveBodies = new List<int>(16);
    private readonly Dictionary<int, Vector2> _bodyMapLocalById = new Dictionary<int, Vector2>(16);
    private readonly Dictionary<int, Vector2> _frozenBodyMapLocalById = new Dictionary<int, Vector2>(16);
    private readonly Dictionary<int, SpriteRenderer> _mapBodyById = new Dictionary<int, SpriteRenderer>(16);
    private readonly Dictionary<byte, SpriteRenderer> _mapDotByPlayerId = new Dictionary<byte, SpriteRenderer>(16);
    private readonly Dictionary<byte, SpriteRenderer> _mapHaloByPlayerId = new Dictionary<byte, SpriteRenderer>(16);

    private Canvas _canvas;
    private CanvasScaler _scaler;
    private RectTransform _window;
    private RectTransform _contentRoot;
    private RawImage _background;
    private RectTransform _iconsRoot;
    private RectTransform _trailsRoot;
    private RectTransform _borderRoot;
    private RectTransform _bodiesRoot;

    private bool _dragging;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartAnchored;
    private float _nextSaveTime;
    private Vector2 _lastSavedAnchored;
    private float _lastSavedScale;

    private MapBehaviour _template;
    private MapBehaviour _lastHideMapInstance;
    private Transform _mapSpace;
    private SpriteRenderer _hereRenderer;
    private SpriteRenderer _bgRenderer;
    private Texture _bgTex;
    private Rect _bgUv;
    private Vector2 _bgBoundsMin;
    private Vector2 _bgBoundsSize;
    private string _currentMapTextureName;
    private float _nextTemplateScanTime;
    private Sprite _iconSprite;
    private bool _iconSpriteSaved;
    private Material _iconBaseMaterial;
    private float _contentAspect = 1f;
    private bool _wasMeeting;
    private bool _wasMapOpen;
    private bool _bigMapMode;
    private float _meetingFreezeNow;
    private float _nextBodyScanTime;
    private bool _freezePlayersForMeeting;
    private bool _freezeBodiesForMeeting;
    private ShipStatus _lastShip;
    private Transform _mapOverlayRoot;

    private const float BigMapBodySize = 0.28f;
    private const float BigMapTrailWidth = 0.006f;
    private const float BigMapDotSize = 0.24f;
    private const float BigMapHaloSize = 0.34f;

    private void GetBigMapIconScales(out float dotSize, out float haloSize, out float bodySize, out float trailWidth)
    {
        dotSize = BigMapDotSize;
        haloSize = BigMapHaloSize;
        bodySize = BigMapBodySize;
        trailWidth = BigMapTrailWidth;

        if (_hereRenderer == null) return;

        try
        {
            var s = _hereRenderer.transform.localScale;
            var baseS = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            if (baseS <= 0.0001f) return;

            dotSize = baseS;
            haloSize = baseS * 1.35f;
            bodySize = baseS * 1.15f;
        }
        catch
        {
        }
    }

    private static bool TryWorldToMapLocal(Vector3 worldPos, out Vector2 mapLocal)
    {
        mapLocal = default;

        if (!Utils.isShip || ShipStatus.Instance == null) return false;

        var pos = worldPos;
        pos /= ShipStatus.Instance.MapScale;
        pos.x *= Mathf.Sign(ShipStatus.Instance.transform.localScale.x);
        mapLocal = new Vector2(pos.x, pos.y);
        return true;
    }

    private void CacheAllPlayerMapLocal()
    {
        if (!Utils.isShip || ShipStatus.Instance == null) return;

        var players = PlayerControl.AllPlayerControls;
        if (players == null) return;

        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null) continue;
            if (p.Data == null) continue;

            var id = p.PlayerId;
            if (!TryWorldToMapLocal(p.transform.position, out var mapLocal)) continue;
            _playerMapLocalById[id] = mapLocal;
        }
    }

    private void EnsureBigMapOverlay()
    {
        if (_mapSpace == null) return;

        // If the existing overlay is not a child of the current _mapSpace it is stale
        // (e.g. RefreshTemplate() picked a new mapSpace). Destroy it and rebuild.
        if (_mapOverlayRoot != null && _mapOverlayRoot.parent != _mapSpace)
        {
            try { Object.Destroy(_mapOverlayRoot.gameObject); } catch { }
            _mapOverlayRoot = null;

            // Also clear the stale segment lists that pointed to GameObjects under the old parent.
            foreach (var kvp in _uiByPlayer)
            {
                var ui = kvp.Value;
                if (ui == null || ui.trail == null) continue;
                ui.trail.mapSegments.Clear();
            }
            _mapBodyById.Clear();
            ClearMapPlayers();
        }

        if (_mapOverlayRoot != null) return;

        var go = new GameObject("MalumMapOverlay");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        go.transform.SetParent(_mapSpace, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        _mapOverlayRoot = go.transform;
    }

    private void SetBigMapOverlayVisible(bool visible)
    {
        if (_mapOverlayRoot == null) return;
        if (_mapOverlayRoot.gameObject.activeSelf == visible) return;
        _mapOverlayRoot.gameObject.SetActive(visible);
    }

    private void ApplyBigMapSorting(SpriteRenderer sr, int extraOrder)
    {
        if (sr == null) return;
        if (_hereRenderer != null)
        {
            try
            {
                sr.sortingLayerID = _hereRenderer.sortingLayerID;
                sr.sortingOrder = _hereRenderer.sortingOrder + extraOrder;
                return;
            }
            catch
            {
            }
        }
        if (_bgRenderer != null)
        {
            try
            {
                sr.sortingLayerID = _bgRenderer.sortingLayerID;
                sr.sortingOrder = _bgRenderer.sortingOrder + extraOrder;
                return;
            }
            catch
            {
            }
        }
        sr.sortingOrder = extraOrder;
    }

    private void UpdateBigMapOverlay()
    {
        if (_mapSpace == null) return;
        EnsureBigMapOverlay();
        SetBigMapOverlayVisible(true);
        UpdateBigMapPlayers();
        UpdateBigMapBodies();
        UpdateBigMapTrails();
    }

    private void UpdateBigMapPlayers()
    {
        if (_mapOverlayRoot == null) return;

        GetBigMapIconScales(out var dotSize, out var haloSize, out _, out _);

        var players = PlayerControl.AllPlayerControls;
        if (players == null)
        {
            ClearMapPlayers();
            return;
        }

        _tmpRemove.Clear();
        foreach (var kvp in _mapDotByPlayerId)
        {
            _tmpRemove.Add(kvp.Key);
        }

        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p.Data == null) continue;

            var id = p.PlayerId;
            RemoveTmp(id);

            var isImp = p.Data.Role != null && p.Data.Role.IsImpostor;
            var isDead = p.Data.IsDead;

            var show = false;
            if (isDead) show = CheatToggles.radarGhosts;
            else if (isImp) show = CheatToggles.radarImps;
            else show = CheatToggles.radarCrew;

            if (!show)
            {
                DisableMapPlayer(id);
                continue;
            }

            Vector2 mapLocal;
            if (_freezePlayersForMeeting && _frozenPlayerMapLocalById.TryGetValue(id, out var frozen))
            {
                mapLocal = frozen;
            }
            else if (_playerMapLocalById.TryGetValue(id, out var cached))
            {
                mapLocal = cached;
            }
            else
            {
                if (!TryWorldToMapLocal(p.transform.position, out mapLocal))
                {
                    DisableMapPlayer(id);
                    continue;
                }
                _playerMapLocalById[id] = mapLocal;
            }

            var dot = GetOrCreateMapDot(id);
            dot.transform.localPosition = new Vector3(mapLocal.x, mapLocal.y, 0f);
            dot.transform.localScale = new Vector3(dotSize, dotSize, 1f);
            if (_iconSprite != null && dot.sprite != _iconSprite) dot.sprite = _iconSprite;
            ApplyMapDotColor(dot, p, isImp, isDead);
            dot.enabled = true;

            var halo = GetOrCreateMapHalo(id);
            var showHalo = CheatToggles.radarImps && isImp && !isDead;
            halo.enabled = showHalo;
            if (showHalo)
            {
                halo.transform.localPosition = new Vector3(mapLocal.x, mapLocal.y, 0f);
                halo.transform.localScale = new Vector3(haloSize, haloSize, 1f);
                halo.color = new Color(1f, 0f, 0f, 0.60f);
            }
        }

        for (var i = 0; i < _tmpRemove.Count; i++)
        {
            var id = _tmpRemove[i];
            DisableMapPlayer(id);
        }
    }

    private SpriteRenderer GetOrCreateMapDot(byte id)
    {
        if (_mapDotByPlayerId.TryGetValue(id, out var sr) && sr != null) return sr;

        var go = new GameObject("Dot");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        go.transform.SetParent(_mapOverlayRoot, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _iconSprite != null ? _iconSprite : GetFallbackSprite();
        ApplyBigMapSorting(sr, 62);
        sr.enabled = false;

        _mapDotByPlayerId[id] = sr;
        return sr;
    }

    private SpriteRenderer GetOrCreateMapHalo(byte id)
    {
        if (_mapHaloByPlayerId.TryGetValue(id, out var sr) && sr != null) return sr;

        var go = new GameObject("Halo");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        go.transform.SetParent(_mapOverlayRoot, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetRadialSprite();
        ApplyBigMapSorting(sr, 61);
        sr.enabled = false;

        _mapHaloByPlayerId[id] = sr;
        return sr;
    }

    private void ApplyMapDotColor(SpriteRenderer sr, PlayerControl p, bool isImp, bool isDead)
    {
        if (sr == null) return;

        var c = ResolvePlayerColor(p, isImp, isDead);

        if (_iconBaseMaterial != null)
        {
            try
            {
                var m = sr.material;
                if (m == null || m.shader != _iconBaseMaterial.shader)
                {
                    sr.material = new Material(_iconBaseMaterial);
                    m = sr.material;
                }

                m.SetColor(PlayerMaterial.BackColor, c);
                m.SetColor(PlayerMaterial.BodyColor, c);
                m.SetColor(PlayerMaterial.VisorColor, Palette.VisorColor);
                sr.color = Color.white;
                return;
            }
            catch { }
        }

        // No player shader available — tint the sprite directly with player color.
        // This gives correct per-player colors even without the shader.
        sr.color = c;
    }

    private void DisableMapPlayer(byte id)
    {
        if (_mapDotByPlayerId.TryGetValue(id, out var dot) && dot != null) dot.enabled = false;
        if (_mapHaloByPlayerId.TryGetValue(id, out var halo) && halo != null) halo.enabled = false;
    }

    private void ClearMapPlayers()
    {
        foreach (var kvp in _mapDotByPlayerId)
        {
            var sr = kvp.Value;
            if (sr == null) continue;
            try { Object.Destroy(sr.gameObject); } catch { }
        }
        foreach (var kvp in _mapHaloByPlayerId)
        {
            var sr = kvp.Value;
            if (sr == null) continue;
            try { Object.Destroy(sr.gameObject); } catch { }
        }
        _mapDotByPlayerId.Clear();
        _mapHaloByPlayerId.Clear();
        _tmpRemove.Clear();
    }

    private void UpdateBigMapBodies()
    {
        if (_mapOverlayRoot == null) return;

        GetBigMapIconScales(out _, out _, out var bodySize, out _);

        var source = _freezeBodiesForMeeting ? _frozenBodyMapLocalById : _bodyMapLocalById;

        _tmpRemoveBodies.Clear();
        foreach (var kvp in _mapBodyById)
        {
            _tmpRemoveBodies.Add(kvp.Key);
        }

        foreach (var kvp in source)
        {
            var id = kvp.Key;
            RemoveTmpBody(id);

            if (!_mapBodyById.TryGetValue(id, out var sr) || sr == null)
            {
                var go = new GameObject("Body");
                go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                go.transform.SetParent(_mapOverlayRoot, false);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetRadialSprite();
                ApplyBigMapSorting(sr, 50);
                _mapBodyById[id] = sr;
            }

            var p = kvp.Value;
            sr.transform.localPosition = new Vector3(p.x, p.y, 0f);
            sr.transform.localScale = new Vector3(bodySize, bodySize, 1f);
            sr.color = new Color(0f, 1f, 0f, 0.85f);
            sr.enabled = true;
        }

        for (var i = 0; i < _tmpRemoveBodies.Count; i++)
        {
            var id = _tmpRemoveBodies[i];
            if (_mapBodyById.TryGetValue(id, out var sr) && sr != null)
            {
                try { Object.Destroy(sr.gameObject); } catch { }
            }
            _mapBodyById.Remove(id);
        }
    }

    private void EnsureBigMapTrailSegments(RadarTrail trail, int needed)
    {
        if (trail == null) return;
        if (trail.mapSegments == null) return;
        if (needed < 0) needed = 0;
        if (needed > TrailMaxSegments) needed = TrailMaxSegments;

        while (trail.mapSegments.Count < needed)
        {
            var go = new GameObject("TrailSeg");
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(_mapOverlayRoot, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetFallbackSprite();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(1f, 1f);
            ApplyBigMapSorting(sr, 40);
            sr.enabled = false;
            trail.mapSegments.Add(sr);
        }
    }

    private void UpdateBigMapTrails()
    {
        if (_mapOverlayRoot == null) return;
        if (!CheatToggles.mapTrails) { DisableAllBigMapTrails(); return; }
        if (!Utils.isShip || ShipStatus.Instance == null) { DisableAllBigMapTrails(); return; }

        var now = _wasMeeting ? _meetingFreezeNow : Time.time;
        var keepSeconds = MapHandler.trailSeconds;
        if (keepSeconds < 5f) keepSeconds = 5f;
        if (keepSeconds > 60f) keepSeconds = 60f;

        var players = PlayerControl.AllPlayerControls;
        if (players == null) { DisableAllBigMapTrails(); return; }

        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p.Data == null) continue;
            if (!_uiByPlayer.TryGetValue(p.PlayerId, out var ui) || ui == null || ui.trail == null) continue;

            var isImp = p.Data.Role != null && p.Data.Role.IsImpostor;
            var isDead = p.Data.IsDead;

            var show = false;
            if (isDead) show = CheatToggles.radarGhosts;
            else if (isImp) show = CheatToggles.radarImps;
            else show = CheatToggles.radarCrew;

            if (!show)
            {
                DisableBigMapTrail(ui.trail);
                continue;
            }

            RenderBigMapTrail(ui.trail, now, keepSeconds);
        }
    }

    private void DisableAllBigMapTrails()
    {
        foreach (var kvp in _uiByPlayer)
        {
            var ui = kvp.Value;
            if (ui == null || ui.trail == null) continue;
            DisableBigMapTrail(ui.trail);
        }
    }

    private static void DisableBigMapTrail(RadarTrail trail)
    {
        if (trail == null || trail.mapSegments == null) return;
        for (var i = 0; i < trail.mapSegments.Count; i++)
        {
            if (trail.mapSegments[i] != null) trail.mapSegments[i].enabled = false;
        }
    }

    private void RenderBigMapTrail(RadarTrail trail, float now, float keepSeconds)
    {
        if (trail == null || trail.mapSegments == null) return;

        GetBigMapIconScales(out _, out _, out _, out var trailWidth);

        var count = trail.count;
        if (count <= 0)
        {
            DisableBigMapTrail(trail);
            return;
        }

        var rawSegments = trail.hasHeadPoint ? count : (count - 1);
        if (rawSegments <= 0)
        {
            DisableBigMapTrail(trail);
            return;
        }

        var needed = rawSegments;
        if (needed > TrailMaxSegments) needed = TrailMaxSegments;

        EnsureBigMapTrailSegments(trail, needed);

        for (var i = 0; i < trail.mapSegments.Count; i++)
        {
            if (trail.mapSegments[i] != null) trail.mapSegments[i].enabled = i < needed;
        }

        var newestIdx = (trail.start + trail.count - 1) % TrailMaxWaypoints;
        var fromLocal = trail.hasHeadPoint ? trail.headPoint : trail.points[newestIdx];
        var idx = newestIdx;
        var remainingLinks = trail.hasHeadPoint ? trail.count : (trail.count - 1);

        for (var i = 0; i < needed; i++)
        {
            var sr = trail.mapSegments[i];
            if (sr == null)
            {
                remainingLinks--;
                continue;
            }

            if (remainingLinks <= 0)
            {
                sr.enabled = false;
                continue;
            }

            Vector2 toLocal;
            float toTime;
            if (trail.hasHeadPoint && i == 0)
            {
                toLocal = trail.points[newestIdx];
                toTime = trail.times[newestIdx];
            }
            else
            {
                idx = (idx - 1 + TrailMaxWaypoints) % TrailMaxWaypoints;
                toLocal = trail.points[idx];
                toTime = trail.times[idx];
            }
            remainingLinks--;

            var d = toLocal - fromLocal;
            var len = d.magnitude;
            if (len < 0.001f)
            {
                sr.enabled = false;
                fromLocal = toLocal;
                continue;
            }

            var mid = (fromLocal + toLocal) * 0.5f;
            var angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            var age = now - toTime;
            var t = keepSeconds > 0.001f ? Mathf.Clamp01(age / keepSeconds) : 1f;
            var alpha = ResolveTrailAlpha(t);

            var invParentScaleY = 1f;
            try
            {
                var ls = _mapOverlayRoot != null ? _mapOverlayRoot.lossyScale : Vector3.one;
                var absY = Mathf.Abs(ls.y);
                if (absY > 0.0001f) invParentScaleY = 1f / absY;
            }
            catch
            {
                invParentScaleY = 1f;
            }

            sr.transform.localPosition = new Vector3(mid.x, mid.y, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            sr.transform.localScale = new Vector3(len, trailWidth * invParentScaleY, 1f);
            sr.color = new Color(trail.color.r, trail.color.g, trail.color.b, alpha);
            sr.enabled = true;

            fromLocal = toLocal;
        }
    }

    private void Update()
    {
        if (MalumMenu.isPanicked)
        {
            SetVisible(false);
            SetBigMapOverlayVisible(false);
            _dragging = false;
            return;
        }

        var map = MapBehaviour.Instance;
        var mapOpen = map != null && map.gameObject != null && map.gameObject.activeInHierarchy;
        if (mapOpen && !_wasMapOpen)
        {
            _wasMapOpen = true;
            try
            {
                RefreshTemplate();
            }
            catch
            {
            }
        }
        else if (!mapOpen && _wasMapOpen)
        {
            _wasMapOpen = false;
        }

        if (!CheatToggles.minimapAlwaysOn && !mapOpen)
        {
            SetVisible(false);
            SetBigMapOverlayVisible(false);
            _dragging = false;
            return;
        }

        if (!Utils.isShip || ShipStatus.Instance == null)
        {
            SetBigMapOverlayVisible(false);
            // Clear stale map texture so old map doesn't show in main menu.
            if (_background != null) _background.texture = null;
            _bgTex = null;
            _currentMapTextureName = null;
            _contentAspect = 1f;
            if (CheatToggles.minimapAlwaysOn && MenuUI.isGUIActive)
            {
                EnsureUi();
                SetVisible(true);
                if (_borderRoot != null) _borderRoot.gameObject.SetActive(true);

                var s2 = Mathf.Clamp(scale, MinScale, MaxScale);
                scale = s2;
                var baseSize2 = BaseSizeAtDefaultScale * (s2 / DefaultScale);
                if (baseSize2 < 64f) baseSize2 = 64f;
                var newSize2 = new Vector2(baseSize2, baseSize2);
                ClampAnchoredPosition(newSize2);
                _window.sizeDelta = newSize2;
                _window.anchoredPosition = anchoredPosition;
                UpdateContentRect();
                HandleDrag();
                MaybeSaveWindow();
            }
            else
            {
                SetVisible(false);
                _dragging = false;
            }
            return;
        }

        var ship = ShipStatus.Instance;
        if (!ReferenceEquals(ship, _lastShip))
        {
            _lastShip = ship;
            ClearBodies();
            ClearPlayers();
            ClearTrails();
        }

        var meeting = Utils.isMeeting;
        if (meeting && CheatToggles.minimapHideDuringMeeting)
        {
            SetVisible(false);
        }
        if (!meeting)
        {
            CacheAllPlayerMapLocal();
        }

        if (meeting && !_wasMeeting)
        {
            _wasMeeting = true;
            _meetingFreezeNow = Time.time;
            SnapshotPlayersForMeeting();
            SnapshotBodiesForMeeting();
        }
        else if (!meeting && _wasMeeting)
        {
            _wasMeeting = false;
            _freezePlayersForMeeting = false;
            _frozenPlayerMapLocalById.Clear();
            _freezeBodiesForMeeting = false;
            _frozenBodyMapLocalById.Clear();
        }

        if (mapOpen)
        {
            EnsureUi();
            SetBigMapOverlayVisible(false);
            _dragging = false;

            if (_template != map || _mapSpace == null || _bgTex == null)
            {
                RefreshTemplate();
            }

            // Hide game's own HereIndicator dots so they don't duplicate our canvas dots.
            SetGameHereIndicatorsVisible(map, false);

            // Stretch radar window to exactly cover the game's Background sprite on screen.
            if (TryGetMapScreenRect(out var mapScreenRect))
            {
                _window.sizeDelta = new Vector2(mapScreenRect.width, mapScreenRect.height);
                var halfSize = new Vector2(mapScreenRect.width * 0.5f, mapScreenRect.height * 0.5f);
                var min = mapScreenRect.center - halfSize;
                _window.anchoredPosition = new Vector2(
                    min.x - Screen.width  * 0.5f,
                    min.y - Screen.height * 0.5f);
                UpdateContentRect();
            }

            // Hide our overlay whenever the game's own settings panel is open.
            var optionsMenu = UnityEngine.Object.FindObjectOfType<OptionsMenuBehaviour>();
            var gameSettingsOpen = optionsMenu != null && optionsMenu.gameObject.activeInHierarchy;

            if (_canvas != null && !_bigMapMode)
                _canvas.sortingOrder = 10;
            _bigMapMode = true;
            SetVisible(!gameSettingsOpen);
            if (_background != null) _background.color = new Color(0f, 0f, 0f, 0f);
            if (_borderRoot != null) _borderRoot.gameObject.SetActive(false);

            UpdatePlayers();
            UpdateBodies();
            UpdateTrails();
            return;
        }

        EnsureUi();
        SetBigMapOverlayVisible(false);
        // Restore game HereIndicators and canvas sort order when map closes.
        _bigMapMode = false;
        if (_canvas != null) _canvas.sortingOrder = 1000;
        if (_lastHideMapInstance != null)
        {
            SetGameHereIndicatorsVisible(_lastHideMapInstance, true);
            _lastHideMapInstance = null;
        }
        if (meeting && CheatToggles.minimapHideDuringMeeting)
        {
            SetVisible(false);
            _dragging = false;
            return;
        }
        SetVisible(true);

        var s = Mathf.Clamp(scale, MinScale, MaxScale);
        scale = s;

        var baseSize = BaseSizeAtDefaultScale * (s / DefaultScale);
        if (baseSize < 64f) baseSize = 64f;

        // Use texture aspect ratio instead of forcing square
        var width = baseSize;
        var height = baseSize;
        if (_contentAspect > 0.001f)
        {
            height = width / _contentAspect;
        }

        var newSize = new Vector2(width, height);

        ClampAnchoredPosition(newSize);
        _window.sizeDelta = newSize;
        _window.anchoredPosition = anchoredPosition;
        _borderRoot.gameObject.SetActive(MenuUI.isGUIActive);
        UpdateContentRect();

        HandleDrag();

        if (Time.unscaledTime >= _nextTemplateScanTime)
        {
            _nextTemplateScanTime = Time.unscaledTime + 1f;
            RefreshTemplate();
        }

        UpdateBackground();
        UpdatePlayers();
        UpdateBodies();
        UpdateTrails();
        MaybeSaveWindow();

        // Press F8 to dump calibration at any time
        if (Input.GetKeyDown(KeyCode.F8))
        {
            SaveCalibrationData();
        }
    }

    private void EnsureUi()
    {
        if (_canvas != null) return;

        var root = new GameObject("MalumRadarCanvas");
        Object.DontDestroyOnLoad(root);

        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000;

        _scaler = root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920f, 1080f);

        root.AddComponent<GraphicRaycaster>();

        if (anchoredPosition.x <= float.MinValue + 1f)
        {
            anchoredPosition = new Vector2(-1920f * 0.5f + 20f, -1080f * 0.5f + 20f);
        }

        var windowGo = new GameObject("RadarWindow");
        _window = windowGo.AddComponent<RectTransform>();
        _window.SetParent(_canvas.transform, false);
        _window.anchorMin = new Vector2(0.5f, 0.5f);
        _window.anchorMax = new Vector2(0.5f, 0.5f);
        _window.pivot = Vector2.zero;
        _window.anchoredPosition = anchoredPosition;

        var contentGo = new GameObject("Content");
        _contentRoot = contentGo.AddComponent<RectTransform>();
        _contentRoot.SetParent(_window, false);
        _contentRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _contentRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _contentRoot.pivot = new Vector2(0.5f, 0.5f);
        _contentRoot.anchoredPosition = Vector2.zero;
        _contentRoot.sizeDelta = _window.sizeDelta;

        var bgGo = new GameObject("Background");
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.SetParent(_contentRoot, false);
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = Vector2.zero;
        _background = bgGo.AddComponent<RawImage>();
        _background.raycastTarget = false;
        _background.color = new Color(0f, 0f, 0f, 0.35f);

        var borderGo = new GameObject("Border");
        _borderRoot = borderGo.AddComponent<RectTransform>();
        _borderRoot.SetParent(_window, false);
        _borderRoot.anchorMin = Vector2.zero;
        _borderRoot.anchorMax = Vector2.one;
        _borderRoot.pivot = new Vector2(0.5f, 0.5f);
        _borderRoot.anchoredPosition = Vector2.zero;
        _borderRoot.sizeDelta = Vector2.zero;
        CreateBorderEdges(_borderRoot);

        var iconsGo = new GameObject("Icons");
        _iconsRoot = iconsGo.AddComponent<RectTransform>();
        _iconsRoot.SetParent(_contentRoot, false);
        _iconsRoot.anchorMin = Vector2.zero;
        _iconsRoot.anchorMax = Vector2.one;
        _iconsRoot.pivot = new Vector2(0.5f, 0.5f);
        _iconsRoot.anchoredPosition = Vector2.zero;
        _iconsRoot.sizeDelta = Vector2.zero;

        var trailsGo = new GameObject("Trails");
        _trailsRoot = trailsGo.AddComponent<RectTransform>();
        _trailsRoot.SetParent(_contentRoot, false);
        _trailsRoot.anchorMin = Vector2.zero;
        _trailsRoot.anchorMax = Vector2.one;
        _trailsRoot.pivot = new Vector2(0.5f, 0.5f);
        _trailsRoot.anchoredPosition = Vector2.zero;
        _trailsRoot.sizeDelta = Vector2.zero;

        _trailsRoot.SetSiblingIndex(1);
        _iconsRoot.SetSiblingIndex(2);

        var bodiesGo = new GameObject("Bodies");
        _bodiesRoot = bodiesGo.AddComponent<RectTransform>();
        _bodiesRoot.SetParent(_contentRoot, false);
        _bodiesRoot.anchorMin = Vector2.zero;
        _bodiesRoot.anchorMax = Vector2.one;
        _bodiesRoot.pivot = new Vector2(0.5f, 0.5f);
        _bodiesRoot.anchoredPosition = Vector2.zero;
        _bodiesRoot.sizeDelta = Vector2.zero;
        _bodiesRoot.SetSiblingIndex(3);
    }

    private void SetVisible(bool visible)
    {
        if (_canvas == null) return;
        if (_canvas.gameObject.activeSelf == visible) return;
        _canvas.gameObject.SetActive(visible);
    }

    private static void CreateBorderEdges(RectTransform parent)
    {
        CreateEdge(parent, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f));
        CreateEdge(parent, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f));
        CreateEdge(parent, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f));
        CreateEdge(parent, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(2f, 0f));
    }

    private void UpdateBorderThickness()
    {
        if (_window == null) return;

        var menuOpen = MenuUI.isGUIActive;
        var thickness = menuOpen ? 10f : 2f;
        var raycast = menuOpen;

        foreach (Transform child in _window)
        {
            var edgeName = child.name;
            if (edgeName == "Top" || edgeName == "Bottom")
            {
                var rt = child as RectTransform;
                if (rt != null) rt.sizeDelta = new Vector2(0f, thickness);
                var img = child.GetComponent<Image>();
                if (img != null)
                {
                    img.raycastTarget = raycast;
                    img.color = menuOpen ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.25f);
                }
            }
            else if (edgeName == "Left" || edgeName == "Right")
            {
                var rt = child as RectTransform;
                if (rt != null) rt.sizeDelta = new Vector2(thickness, 0f);
                var img = child.GetComponent<Image>();
                if (img != null)
                {
                    img.raycastTarget = raycast;
                    img.color = menuOpen ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.25f);
                }
            }
        }
    }

    private static void CreateEdge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 sizeDelta, bool raycastTarget = false)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = sizeDelta;
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.25f);
        img.raycastTarget = raycastTarget;
    }

    private void HandleDrag()
    {
        if (_window == null) return;

        if (Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            return;
        }

        if (!_dragging)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_window, Input.mousePosition, null)) return;
            _dragging = true;
            _dragStartMouse = Input.mousePosition;
            _dragStartAnchored = anchoredPosition;
            return;
        }

        if (!Input.GetMouseButton(0))
        {
            _dragging = false;
            return;
        }

        var sf = _scaler != null ? _scaler.scaleFactor : 1f;
        if (sf < 0.0001f) sf = 1f;
        var delta = (Vector2)Input.mousePosition - _dragStartMouse;
        anchoredPosition = _dragStartAnchored + (delta / sf);
    }

    private static float ResolveTrailAlpha(float normalizedAge)
    {
        if (normalizedAge <= TrailFadeStart) return TrailAlphaStart;
        var fade = Mathf.InverseLerp(TrailFadeStart, 1f, normalizedAge);
        return Mathf.Lerp(TrailAlphaStart, TrailAlphaEnd, fade);
    }

    private static void ClampAnchoredPosition(Vector2 size)
    {
        var halfWidth = Screen.width * 0.5f;
        var halfHeight = Screen.height * 0.5f;
        if (halfWidth <= 0f || halfHeight <= 0f) return;

        var marginX = 8f;
        var marginY = 8f;
        anchoredPosition = new Vector2(
            Mathf.Clamp(anchoredPosition.x, -halfWidth + marginX, halfWidth - size.x - marginX),
            Mathf.Clamp(anchoredPosition.y, -halfHeight + marginY, halfHeight - size.y - marginY)
        );
    }

    // Converts a screen-space mouse position to map-local coordinates (inverse of TryMapLocalToAnchored).
    // Returns false if the radar background data is not yet initialized.
    private bool TryScreenToMapLocal(Vector2 screenPos, out Vector2 mapLocal)
    {
        mapLocal = default;

        if (_contentRoot == null) return false;
        if (_bgRenderer == null) return false;
        if (_bgRenderer.sprite == null) return false;
        if (_mapSpace == null) return false;

        var bsize = _bgBoundsSize;
        if (bsize.x <= 0.0001f || bsize.y <= 0.0001f) return false;

        // Step 1: Convert screen position to content-root local position.
        var isInsideContent = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _contentRoot, screenPos, null, out var contentLocal);
        if (!isInsideContent) return false;

        // Step 2: Convert content-root local to UV [0..1] range.
        var rect = _contentRoot.rect;
        var w = rect.width;
        var h = rect.height;
        if (w <= 0.0001f || h <= 0.0001f) return false;

        // anchoredPosition = (u - 0.5) * w  =>  u = (anchoredPos / w) + 0.5
        var u = (contentLocal.x / w) + 0.5f;
        var v = (contentLocal.y / h) + 0.5f;

        // Step 3: UV to bg-renderer local position.
        var bgLocal = new Vector3(
            _bgBoundsMin.x + u * bsize.x,
            _bgBoundsMin.y + v * bsize.y,
            0f);

        // Step 4: bg-renderer local -> world -> map-space local.
        var world = _bgRenderer.transform.TransformPoint(bgLocal);
        var mapSpaceLocal = _mapSpace.InverseTransformPoint(world);
        mapLocal = new Vector2(mapSpaceLocal.x, mapSpaceLocal.y);
        return true;
    }


    private static string GetMapTextureNameFromMapId(byte mapId)
    {
        return mapId switch
        {
            0 => "map",
            1 => "map_HQ",
            2 => "mapPB",
            3 => "map4_airship",
            4 => "FungleMinimap",
            _ => ""
        };
    }

    private static string GetMapTextureNameFromShipStatus(byte mapId, out string reason)
    {
        reason = "options";
        var mapName = GetMapTextureNameFromMapId(mapId);

        try
        {
            var ship = ShipStatus.Instance;
            if (ship == null) return mapName;

            var scale = ship.MapScale;
            if (Mathf.Abs(scale - 8.1125f) <= 0.05f)
            {
                reason = $"ship-scale:{scale:F4}";
                return "map4_airship";
            }

            if (Mathf.Abs(scale - 6f) <= 0.05f)
            {
                reason = $"ship-scale:{scale:F4}";
                return "FungleMinimap";
            }

            if (Mathf.Abs(scale - 4.975f) <= 0.05f)
            {
                reason = $"ship-scale:{scale:F4}";
                return "mapPB";
            }

            if (Mathf.Abs(scale - 5.486f) <= 0.05f)
            {
                reason = $"ship-scale:{scale:F4}";
                return "map_HQ";
            }
        }
        catch { }

        return mapName;
    }

    private void RefreshTemplate()
    {
        var template = MapBehaviour.Instance ?? Object.FindObjectOfType<MapBehaviour>(true);
        string mapName = "";
        try { mapName = (template != null && template.gameObject != null) ? template.name?.Replace("(Clone)", "").Trim() ?? "" : ""; } catch { mapName = ""; }

        if (string.IsNullOrEmpty(mapName) && ShipStatus.Instance != null)
        {
            var mapId = Utils.GetCurrentMapID();
            mapName = GetMapTextureNameFromShipStatus(mapId, out _);
        }
        else if (!string.IsNullOrEmpty(mapName))
        {
            // Convert MapBehaviour name to texture name
            mapName = mapName switch
            {
                "ShipMap" => "map",
                "HqMap" => "map_HQ",
                "PbMap" => "mapPB",
                "AirshipMap" => "map4_airship",
                "FungleMap" => "FungleMinimap",
                _ => mapName
            };
        }



        // Try to load from embedded config first
        var configs = RadarMapConfig.GetDefaultConfigs();
        RadarMapConfig? config = null;
        foreach (var cfg in configs)
        {
            if (string.Equals(mapName, cfg.TextureName, StringComparison.OrdinalIgnoreCase))
            {
                config = cfg;
                break;
            }
        }


        // Try embedded texture if config found
        if (config.HasValue)
        {
            // If template is null (dropship / lobby) and the map hasn't changed, keep existing
            // calibrated state rather than clobbering _mapSpace with null.
            // If the map HAS changed, fall through to load the new map's texture+bounds.
            if (_bgTex != null &&
                string.Equals(_currentMapTextureName, config.Value.TextureName, StringComparison.OrdinalIgnoreCase) &&
                (template == null || ReferenceEquals(_template, template)))
                return;

            var embeddedTex = LoadEmbeddedTexture(config.Value.TextureName + ".png");
            if (embeddedTex != null)
            {
                _template = template;
                _currentMapTextureName = config.Value.TextureName;
                _hereRenderer = template?.HerePoint != null ? template.HerePoint : null;
                try { _mapSpace = _hereRenderer != null ? _hereRenderer.transform.parent : null; } catch { _mapSpace = null; }
                try { if (_mapSpace == null) _mapSpace = template?.transform; } catch { _mapSpace = null; }

                // Try to grab the game's own Background renderer directly by name.
                // go=Background, sprite=map is the map image used by the game — same source
                // as our embedded texture, so we can use its transform to compute exact bounds.
                SpriteRenderer bgRenderer = null;
                if (template != null)
                {
                    var allSRs = template.GetComponentsInChildren<SpriteRenderer>(true);
                    foreach (var bgSr in allSRs)
                    {
                        if (bgSr == null || bgSr.sprite == null) continue;
                        var goName = bgSr.gameObject?.name ?? "";
                        if (goName.Equals("Background", StringComparison.OrdinalIgnoreCase))
                        {
                            bgRenderer = bgSr;
                            break;
                        }
                    }
                }

                if (bgRenderer != null && bgRenderer.sprite != null && _mapSpace != null)
                {
                    // Use the game's live texture directly — same image, zero calibration error.
                    var liveTex = bgRenderer.sprite.texture;
                    if (liveTex != null) _bgTex = liveTex;
                    else _bgTex = embeddedTex;

                    _bgUv = new Rect(0, 0, 1, 1);
                    _bgRenderer = null; // use direct-UV path in TryMapLocalToAnchored

                    // Compute BoundsMin/BoundsSize in _mapSpace local coords.
                    // sprite.bounds is in sprite-local space; apply lossyScale to get world-space half-extents.
                    var sprBounds = bgRenderer.sprite.bounds;
                    var ls = bgRenderer.transform.lossyScale;
                    var halfW = sprBounds.extents.x * Mathf.Abs(ls.x);
                    var halfH = sprBounds.extents.y * Mathf.Abs(ls.y);
                    var bgWorldCenter = bgRenderer.transform.TransformPoint(sprBounds.center);
                    var worldMin = new Vector3(bgWorldCenter.x - halfW, bgWorldCenter.y - halfH, bgWorldCenter.z);
                    var worldMax = new Vector3(bgWorldCenter.x + halfW, bgWorldCenter.y + halfH, bgWorldCenter.z);
                    var bgLocalMin = _mapSpace.InverseTransformPoint(worldMin);
                    var bgLocalMax = _mapSpace.InverseTransformPoint(worldMax);
                    _bgBoundsMin = new Vector2(Mathf.Min(bgLocalMin.x, bgLocalMax.x), Mathf.Min(bgLocalMin.y, bgLocalMax.y));
                    _bgBoundsSize = new Vector2(Mathf.Abs(bgLocalMax.x - bgLocalMin.x), Mathf.Abs(bgLocalMax.y - bgLocalMin.y));
                    _contentAspect = _bgBoundsSize.y > 0.0001f ? (_bgBoundsSize.x / _bgBoundsSize.y) : 1f;
                }
                else
                {
                    // Map closed or Background not found yet — use embedded texture + config bounds.
                    _bgTex = embeddedTex;
                    _bgUv = config.Value.UvRect;
                    _bgRenderer = null;
                    _bgBoundsMin = config.Value.BoundsMin;
                    _bgBoundsSize = config.Value.BoundsSize;
                    _contentAspect = config.Value.BoundsSize.y > 0.0001f ? (config.Value.BoundsSize.x / config.Value.BoundsSize.y) : 1f;
                }

                // Always grab material from live HerePoint renderer — this is what gives
                // per-player coloring. The sprite can be embedded, but the shader MUST
                // come from the game's player material.
                _iconSprite = _hereRenderer?.sprite;
                _iconBaseMaterial = _hereRenderer?.sharedMaterial;

                // If _hereRenderer not available yet, try scanning the scene for ANY
                // SpriteRenderer using the player body shader so we can steal the material.
                if (_iconBaseMaterial == null)
                {
                    try
                    {
                        var allSRs = Object.FindObjectsOfType<SpriteRenderer>(true);
                        foreach (var spriteRenderer in allSRs)
                        {
                            if (spriteRenderer == null || spriteRenderer.sharedMaterial == null) continue;
                            var shaderName = spriteRenderer.sharedMaterial.shader?.name ?? "";
                            if (shaderName.Contains("PlayerMaterial") || shaderName.Contains("Player"))
                            {
                                _iconBaseMaterial = spriteRenderer.sharedMaterial;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                // Sprite: use embedded icon if available, else fallback
                if (_iconSprite == null)
                {
                    try
                    {
                        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        var resourceName = "MalumMenu.Resources.radar_crewmate_icon.png";
                        using var stream = assembly.GetManifestResourceStream(resourceName);
                        if (stream != null)
                        {
                            var buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            var tex = new Texture2D(2, 2);
                            tex.LoadImage(buffer);
                            _iconSprite = CreateSpriteFromTexture(tex);
                        }
                        else
                        {
                            _iconSprite = GetFallbackIcon();
                        }
                    }
                    catch (Exception ex)
                    {
                        _iconSprite = GetFallbackIcon();
                        MalumMenu.Log?.LogWarning($"Radar.RefreshTemplate: Failed to load embedded crewmate icon: {ex.Message}, using fallback circle");
                    }
                }

                // Save icon sprite when it becomes available
                if (_iconSprite != null && !_iconSpriteSaved)
                {
                    _iconSpriteSaved = true;
                    SaveCrewmateIcon();
                }
                return;
            }
        }

        // Fall back to dynamic extraction
        if (template == null)
        {
            if (_template != null) return;
            _template = null;
            _currentMapTextureName = null;
            _mapSpace = null;
            _hereRenderer = null;
            _bgRenderer = null;
            _bgTex = null;
            _bgUv = default;
            _bgBoundsMin = default;
            _bgBoundsSize = default;
            _contentAspect = 1f;
            _iconSprite = null;
            return;
        }

        if (_template == template)
        {
            var ok = _mapSpace != null && _bgRenderer != null && _bgRenderer.sprite != null && _bgRenderer.sprite.texture != null;
            if (ok) return;
        }
        _template = template;

        _hereRenderer = template.HerePoint != null ? template.HerePoint : null;
        _mapSpace = _hereRenderer != null ? _hereRenderer.transform.parent : null;
        if (_mapSpace == null) _mapSpace = template.transform;


        var sr = ResolveBackgroundRenderer(template, null);
        if (sr == null) sr = ResolveBackgroundRenderer(template, _mapSpace);
        if (sr == null || sr.sprite == null || sr.sprite.texture == null)
        {
            _bgRenderer = null;
            _bgTex = null;
            _bgUv = default;
            _bgBoundsMin = default;
            _bgBoundsSize = default;
            _contentAspect = 1f;
            _iconSprite = null;
            return;
        }

        _bgRenderer = sr;
        if (_mapSpace == null) _mapSpace = sr.transform.parent;
        if (_mapSpace == null) _mapSpace = template.transform;

        _bgTex = sr.sprite.texture;
        var tr = sr.sprite.textureRect;
        var texW = sr.sprite.texture.width;
        var texH = sr.sprite.texture.height;
        _bgUv = new Rect(tr.x / texW, tr.y / texH, tr.width / texW, tr.height / texH);
        var b = sr.sprite.bounds;
        _bgBoundsMin = b.min;
        _bgBoundsSize = b.size;
        _contentAspect = tr.height > 0.0001f ? (tr.width / tr.height) : 1f;

        _iconSprite = _hereRenderer != null ? _hereRenderer.sprite : null;
        _iconBaseMaterial = _hereRenderer != null ? _hereRenderer.sharedMaterial : null;

        // Save icon sprite when it becomes available
        if (_iconSprite != null && !_iconSpriteSaved)
        {
            _iconSpriteSaved = true;
            SaveCrewmateIcon();
        }

    }

    private static SpriteRenderer ResolveBackgroundRenderer(MapBehaviour template, Transform mapSpace)
    {
        if (template == null) return null;

        SpriteRenderer[] renderers = null;
        try
        {
            if (mapSpace != null) renderers = mapSpace.GetComponentsInChildren<SpriteRenderer>(true);
            else renderers = template.GetComponentsInChildren<SpriteRenderer>(true);
        }
        catch
        {
        }
        if (renderers == null) return null;

        var bans = "";
        try
        {
            if (MalumMenu.minimapBgBan != null) bans = MalumMenu.minimapBgBan.Value;
        }
        catch
        {
            bans = "";
        }

        var here = template.HerePoint;
        var hereSprite = here != null ? here.sprite : null;


        SpriteRenderer best = null;
        var bestPixelArea = 0f;
        var bestWorldArea = 0f;
        var bestOrder = int.MaxValue;

        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var s = r.sprite;
            if (s == null) continue;
            var tex = s.texture;
            if (tex == null) continue;

            if (here != null && ReferenceEquals(r, here)) continue;
            if (hereSprite != null && ReferenceEquals(s, hereSprite)) continue;

            var n = r.gameObject != null ? r.gameObject.name : "";
            var ln = n != null ? n.ToLowerInvariant() : "";

            if (ln.Contains("overlay") || ln.Contains("highlight") || ln.Contains("room") || ln.Contains("fog")) continue;
            if (ln.Contains("here") || ln.Contains("player") || ln.Contains("icon")) continue;

            var b = s.bounds.size;
            var sx = Mathf.Abs(r.transform.lossyScale.x);
            var sy = Mathf.Abs(r.transform.lossyScale.y);
            var worldW = b.x * sx;
            var worldH = b.y * sy;
            var worldArea = worldW * worldH;

            if (worldArea <= 0.0001f) continue;

            var tr = s.textureRect;
            var pixelArea = tr.width * tr.height;
            var order = r.sortingOrder;

            var spriteName = s != null ? s.name : "";
            var banned = IsBannedByTokenList(bans, n) || IsBannedByTokenList(bans, spriteName);
            if (banned) continue;

            var better = false;
            if (pixelArea > bestPixelArea * 1.05f)
            {
                better = true;
            }
            else if (pixelArea >= bestPixelArea * 0.95f)
            {
                if (worldArea > bestWorldArea * 1.05f)
                {
                    better = true;
                }
                else if (worldArea >= bestWorldArea * 0.95f && order < bestOrder)
                {
                    better = true;
                }
            }

            if (!better) continue;

            best = r;
            bestPixelArea = pixelArea;
            bestWorldArea = worldArea;
            bestOrder = order;
        }

        return best;
    }

    private static bool IsBannedByTokenList(string tokens, string name)
    {
        if (string.IsNullOrWhiteSpace(tokens)) return false;
        if (string.IsNullOrEmpty(name)) return false;

        var ln = name.ToLowerInvariant();
        var parts = tokens.Split(new[] { ',', ';', '|', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (string.IsNullOrWhiteSpace(p)) continue;
            var t = p.Trim().ToLowerInvariant();
            if (t.Length == 0) continue;
            if (ln.Contains(t)) return true;
        }
        return false;
    }

    private void SetGameHereIndicatorsVisible(MapBehaviour map, bool visible)
    {
        if (map == null) return;
        try
        {
            if (!visible) _lastHideMapInstance = map;
            var allSRs = map.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in allSRs)
            {
                if (sr == null) continue;
                var n = sr.gameObject?.name ?? "";
                if (n.StartsWith("HereIndicator", StringComparison.OrdinalIgnoreCase))
                    sr.enabled = visible;
            }
        }
        catch { }
    }

    private bool TryGetMapScreenRect(out Rect screenRect)
    {
        screenRect = default;
        try
        {
            var map = MapBehaviour.Instance;
            if (map == null) return false;

            SpriteRenderer bgSr = null;
            var allSRs = map.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in allSRs)
            {
                if (sr == null) continue;
                var goName = sr.gameObject?.name ?? "";
                if (goName.Equals("Background", StringComparison.OrdinalIgnoreCase))
                {
                    bgSr = sr;
                    break;
                }
            }
            if (bgSr == null) return false;

            var cam = Camera.main;
            if (cam == null) return false;

            var b = bgSr.bounds;
            var corners = new Vector3[]
            {
                new Vector3(b.min.x, b.min.y, b.center.z),
                new Vector3(b.max.x, b.min.y, b.center.z),
                new Vector3(b.max.x, b.max.y, b.center.z),
                new Vector3(b.min.x, b.max.y, b.center.z),
            };

            var minX = float.MaxValue; var minY = float.MaxValue;
            var maxX = float.MinValue; var maxY = float.MinValue;
            foreach (var c in corners)
            {
                var sp = cam.WorldToScreenPoint(c);
                if (sp.x < minX) minX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y > maxY) maxY = sp.y;
            }

            var sf = _scaler != null ? _scaler.scaleFactor : 1f;
            if (sf < 0.0001f) sf = 1f;
            screenRect = new Rect(minX / sf, minY / sf, (maxX - minX) / sf, (maxY - minY) / sf);
            return screenRect.width > 1f && screenRect.height > 1f;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateBackground()
    {
        if (_background == null) return;

        if (_bgTex != null)
        {
            _background.texture = _bgTex;
            _background.uvRect = _bgUv;
            _background.color = new Color(1f, 1f, 1f, 0.95f);
        }
        else
        {
            _background.texture = null;
            _background.uvRect = new Rect(0f, 0f, 1f, 1f);
            _background.color = new Color(0f, 0f, 0f, 0.35f);
        }
    }

    private void UpdateContentRect()
    {
        if (_window == null) return;
        if (_contentRoot == null) return;

        var win = _window.rect;
        if (win.width <= 0.0001f || win.height <= 0.0001f) return;

        var aspect = _contentAspect;
        if (aspect < 0.0001f) aspect = 1f;

        var w = win.width;
        var h = win.height;

        var targetW = w;
        var targetH = w / aspect;
        if (targetH > h)
        {
            targetH = h;
            targetW = h * aspect;
        }

        _contentRoot.sizeDelta = new Vector2(targetW, targetH);
        _contentRoot.anchoredPosition = Vector2.zero;
    }

    private static Sprite GetFallbackSprite()
    {
        if (_fallbackSprite != null) return _fallbackSprite;
        _fallbackSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return _fallbackSprite;
    }

    private static Sprite GetRadialSprite()
    {
        if (_radialSprite != null) return _radialSprite;

        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var cx = 31.5f;
        var cy = 31.5f;
        var r0 = 10f;
        var r1 = 31.5f;

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = Mathf.Sqrt((dx * dx) + (dy * dy));
                var t = Mathf.InverseLerp(r0, r1, d);
                var a = 1f - Mathf.Clamp01(t);
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        _radialSprite = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        return _radialSprite;
    }

    private bool TryMapLocalToAnchored(Vector2 mapLocal, out Vector2 anchored)
    {
        anchored = default;

        var bsize = _bgBoundsSize;
        if (bsize.x <= 0.0001f || bsize.y <= 0.0001f) return false;

        float u, v;
        Vector3 bgLocal = Vector3.zero;

        // When using embedded textures, convert directly from map local to UV
        if (_bgRenderer == null)
        {
            // Direct conversion: map local is already in the same coordinate space as bounds
            u = (mapLocal.x - _bgBoundsMin.x) / bsize.x;
            v = (mapLocal.y - _bgBoundsMin.y) / bsize.y;

        }
        else
        {
            // Original logic for game renderer
            if (_bgRenderer.sprite == null) return false;
            if (_mapSpace == null) return false;

            var world = _mapSpace.TransformPoint(new Vector3(mapLocal.x, mapLocal.y, 0f));
            bgLocal = _bgRenderer.transform.InverseTransformPoint(world);

            u = (bgLocal.x - _bgBoundsMin.x) / bsize.x;
            v = (bgLocal.y - _bgBoundsMin.y) / bsize.y;
        }

        var rect = _contentRoot != null ? _contentRoot.rect : _window.rect;
        var w = rect.width;
        var h = rect.height;
        if (w <= 0.0001f || h <= 0.0001f) return false;

        anchored = new Vector2((Mathf.Clamp01(u) - 0.5f) * w, (Mathf.Clamp01(v) - 0.5f) * h);
        return true;
    }

    private void UpdatePlayers()
    {
        if (_iconsRoot == null) return;


        _tmpRemove.Clear();
        foreach (var kvp in _uiByPlayer)
        {
            _tmpRemove.Add(kvp.Key);
        }

        var players = PlayerControl.AllPlayerControls;
        if (players != null)
        {
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.Data == null) continue;

                var id = p.PlayerId;
                RemoveTmp(id);

                if (!_uiByPlayer.TryGetValue(id, out var ui) || ui == null)
                {
                    CreatePlayerUi(id);
                }

                UpdatePlayerUi(p);
            }
        }

        for (var i = 0; i < _tmpRemove.Count; i++)
        {
            var id = _tmpRemove[i];
            if (_uiByPlayer.TryGetValue(id, out var ui) && ui != null)
            {
                DestroyPlayerUi(ui);
            }
            _uiByPlayer.Remove(id);
        }
    }

    private void RemoveTmp(byte id)
    {
        for (var i = 0; i < _tmpRemove.Count; i++)
        {
            if (_tmpRemove[i] != id) continue;
            _tmpRemove.RemoveAt(i);
            return;
        }
    }

    private void CreatePlayerUi(byte id)
    {
        var ui = new RadarPlayerUi();
        ui.id = id;
        ui.trail = new RadarTrail(TrailMaxWaypoints);

        ui.highlight = CreateHalo(_iconsRoot, "Halo", HighlightSize);
        ui.dot = CreateIcon(_iconsRoot, "Dot", IconSize);

        ui.highlight.enabled = false;
        ui.dot.enabled = false;

        _uiByPlayer[id] = ui;
    }

    private Image CreateIcon(RectTransform parent, string name, float size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = _iconSprite != null ? _iconSprite : GetFallbackSprite();
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    private static Image CreateHalo(RectTransform parent, string name, float size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = GetRadialSprite();
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    private static void DestroyPlayerUi(RadarPlayerUi ui)
    {
        if (ui == null) return;
        if (ui.highlight != null) { try { Object.Destroy(ui.highlight.gameObject); } catch { } }
        if (ui.dot != null) { try { Object.Destroy(ui.dot.gameObject); } catch { } }
        if (ui.trail != null)
        {
            for (var i = 0; i < ui.trail.segments.Count; i++)
            {
                var seg = ui.trail.segments[i];
                if (seg == null) continue;
                try { Object.Destroy(seg.gameObject); } catch { }
            }
            ui.trail.segments.Clear();

            for (var i = 0; i < ui.trail.mapSegments.Count; i++)
            {
                var seg = ui.trail.mapSegments[i];
                if (seg == null) continue;
                try { Object.Destroy(seg.gameObject); } catch { }
            }
            ui.trail.mapSegments.Clear();
        }
    }

    private void UpdatePlayerUi(PlayerControl p)
    {
        if (p == null || p.Data == null) return;

        var id = p.PlayerId;
        if (!_uiByPlayer.TryGetValue(id, out var ui) || ui == null) return;

        var isImp = p.Data.Role != null && p.Data.Role.IsImpostor;
        var isDead = p.Data.IsDead;

        var show = false;
        if (isDead) show = CheatToggles.radarGhosts;
        else if (isImp) show = CheatToggles.radarImps;
        else show = CheatToggles.radarCrew;

        if (!show)
        {
            if (ui.dot != null) ui.dot.enabled = false;
            if (ui.highlight != null) ui.highlight.enabled = false;
            return;
        }

        if (!Utils.isShip || ShipStatus.Instance == null)
        {
            if (ui.dot != null) ui.dot.enabled = false;
            if (ui.highlight != null) ui.highlight.enabled = false;
            return;
        }

        if (_iconSprite != null)
        {
            if (ui.dot != null && ui.dot.sprite != _iconSprite) ui.dot.sprite = _iconSprite;
        }

        if (_iconBaseMaterial != null && ui.dot != null)
        {
            try
            {
                var m = ui.dot.material;
                if (m == null || m.shader != _iconBaseMaterial.shader)
                {
                    ui.dot.material = new Material(_iconBaseMaterial);
                }
            }
            catch
            {
            }
        }

        var s = Mathf.Clamp(scale, MinScale, MaxScale);
        var iconScale = 1f;
        try
        {
            if (MalumMenu.minimapIconScale != null) iconScale = MalumMenu.minimapIconScale.Value;
        }
        catch
        {
            iconScale = 1f;
        }
        if (iconScale < 0.50f) iconScale = 0.50f;
        if (iconScale > 2.50f) iconScale = 2.50f;

        float scaleMul;
        if (_bigMapMode && _contentRoot != null && _contentRoot.sizeDelta.x > 0.1f)
        {
            scaleMul = (_contentRoot.sizeDelta.x / BaseSizeAtDefaultScale) * 0.6f * iconScale;
        }
        else
        {
            scaleMul = (s / DefaultScale) * iconScale;
        }
        var dotSize = IconSize * scaleMul;
        var haloSize = HighlightSize * scaleMul * HaloSizeMult;

        if (ui.dot != null && Mathf.Abs(ui.lastDotSize - dotSize) > 0.01f)
        {
            ui.dot.rectTransform.sizeDelta = new Vector2(dotSize, dotSize);
            ui.lastDotSize = dotSize;
        }
        if (ui.highlight != null && Mathf.Abs(ui.lastHighlightSize - haloSize) > 0.01f)
        {
            ui.highlight.rectTransform.sizeDelta = new Vector2(haloSize, haloSize);
            ui.lastHighlightSize = haloSize;
        }

        Vector2 mapLocal;
        if (_freezePlayersForMeeting && _frozenPlayerMapLocalById.TryGetValue(id, out var frozen))
        {
            mapLocal = frozen;
        }
        else
        {
            if (!TryWorldToMapLocal(p.transform.position, out mapLocal))
            {
                if (ui.dot != null) ui.dot.enabled = false;
                if (ui.highlight != null) ui.highlight.enabled = false;
                return;
            }
            _playerMapLocalById[id] = mapLocal;
        }

        if (ui.trail != null && _freezePlayersForMeeting)
        {
            ui.trail.headPoint = mapLocal;
            ui.trail.hasHeadPoint = true;
        }

        if (!TryMapLocalToAnchored(mapLocal, out var anchored))
        {
            if (ui.dot != null) ui.dot.enabled = false;
            if (ui.highlight != null) ui.highlight.enabled = false;
            return;
        }

        var color = ResolvePlayerColor(p, isImp, isDead);
        if (ui.dot != null)
        {
            ui.dot.enabled = true;
            ui.dot.rectTransform.anchoredPosition = anchored;
            ApplyPlayerIconColors(ui.dot, color, isImp, isDead);
        }

        if (ui.highlight != null)
        {
            var highlight = CheatToggles.radarImps && isImp && !isDead;
            ui.highlight.enabled = highlight;
            if (highlight)
            {
                ui.highlight.rectTransform.anchoredPosition = anchored;
                ui.highlight.color = new Color(1f, 0f, 0f, 0.60f);
            }
        }

        if (ui.trail != null)
        {
            ui.trail.color = color;
        }
    }

    private static Color ResolvePlayerColor(PlayerControl p, bool isImp, bool isDead)
    {
        if (p == null || p.Data == null) return Color.white;

        if (isDead)
        {
            if (CheatToggles.radarColorBased) return SafePlayerColor(p);
            return new Color(0.7f, 0.7f, 0.7f, 1f); // Grey for dead (like anonymous votes)
        }

        if (CheatToggles.radarColorBased) return SafePlayerColor(p);
        if (isImp) return Color.red;
        return Color.blue; // Blue for crewmates
    }

    private static void ApplyPlayerIconColors(Image img, Color color, bool isImp, bool isDead)
    {
        if (img == null) return;

        var mat = img.material;
        if (mat != null)
        {
            try
            {
                mat.SetColor(PlayerMaterial.BackColor, color);
                mat.SetColor(PlayerMaterial.BodyColor, color);
                mat.SetColor(PlayerMaterial.VisorColor, Palette.VisorColor);
                img.color = Color.white;
                return;
            }
            catch
            {
            }
        }

        img.color = color;
    }

    private void UpdateBodies()
    {
        if (_bodiesRoot == null) return;
        if (!CheatToggles.minimapAlwaysOn) return;
        if (!Utils.isShip || ShipStatus.Instance == null) return;

        if (_freezeBodiesForMeeting)
        {
            UpdateBodiesFrozen();
            return;
        }

        if (Time.unscaledTime < _nextBodyScanTime) return;
        _nextBodyScanTime = Time.unscaledTime + 0.5f;

        _tmpRemoveBodies.Clear();
        foreach (var kvp in _bodyUiById)
        {
            _tmpRemoveBodies.Add(kvp.Key);
        }

        DeadBody[] bodies = null;
        try { bodies = Object.FindObjectsOfType<DeadBody>(true); } catch { bodies = null; }
        if (bodies != null)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                if (b.gameObject == null) continue;
                if (!b.gameObject.activeInHierarchy) continue;

                var id = b.gameObject.GetInstanceID();
                RemoveTmpBody(id);

                if (!_bodyUiById.TryGetValue(id, out var img) || img == null)
                {
                    img = CreateHalo(_bodiesRoot, "Body", BodySize);
                    _bodyUiById[id] = img;
                }

                if (!TryWorldToMapLocal(b.transform.position, out var mapLocal))
                {
                    img.enabled = false;
                    continue;
                }
                _bodyMapLocalById[id] = mapLocal;

                if (!TryMapLocalToAnchored(mapLocal, out var anchored))
                {
                    img.enabled = false;
                    continue;
                }

                var s = Mathf.Clamp(scale, MinScale, MaxScale);
                var iconScale = 1f;
                try
                {
                    if (MalumMenu.minimapIconScale != null) iconScale = MalumMenu.minimapIconScale.Value;
                }
                catch
                {
                    iconScale = 1f;
                }
                if (iconScale < 0.50f) iconScale = 0.50f;
                if (iconScale > 2.50f) iconScale = 2.50f;

                var size = BodySize * (s / DefaultScale) * iconScale;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                img.rectTransform.anchoredPosition = anchored;
                img.color = new Color(0f, 1f, 0f, 0.85f);
                img.enabled = true;
            }
        }

        for (var i = 0; i < _tmpRemoveBodies.Count; i++)
        {
            var id = _tmpRemoveBodies[i];
            if (_bodyUiById.TryGetValue(id, out var img) && img != null)
            {
                try { Object.Destroy(img.gameObject); } catch { }
            }
            _bodyUiById.Remove(id);
            _bodyMapLocalById.Remove(id);
        }
    }

    private void SnapshotBodiesForMeeting()
    {
        _freezeBodiesForMeeting = true;

        _frozenBodyMapLocalById.Clear();
        foreach (var kvp in _bodyMapLocalById)
        {
            _frozenBodyMapLocalById[kvp.Key] = kvp.Value;
        }
    }

    private static bool TryGetTrailPointStepsBack(RadarTrail trail, int stepsBack, out Vector2 mapLocal)
    {
        mapLocal = default;
        if (trail == null) return false;
        if (trail.count <= 0) return false;
        if (stepsBack < 0) stepsBack = 0;

        var newestIdx = (trail.start + trail.count - 1) % TrailMaxWaypoints;
        var idx = newestIdx;
        for (var i = 0; i < stepsBack; i++)
        {
            if (trail.count <= (i + 1)) break;
            idx = (idx - 1 + TrailMaxWaypoints) % TrailMaxWaypoints;
        }

        mapLocal = trail.points[idx];
        return true;
    }

    private void SnapshotPlayersForMeeting()
    {
        _freezePlayersForMeeting = true;

        _frozenPlayerMapLocalById.Clear();
        var players = PlayerControl.AllPlayerControls;
        if (players != null)
        {
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.Data == null) continue;

                var id = p.PlayerId;

                Vector2 mapLocal;
                if (_uiByPlayer.TryGetValue(id, out var ui) && ui != null && ui.trail != null &&
                    TryGetTrailPointStepsBack(ui.trail, MeetingFreezeStepsBack, out mapLocal))
                {
                }
                else if (_playerMapLocalById.TryGetValue(id, out var cached))
                {
                    mapLocal = cached;
                }
                else if (!TryWorldToMapLocal(p.transform.position, out mapLocal))
                {
                    continue;
                }

                _frozenPlayerMapLocalById[id] = mapLocal;
            }
        }

        foreach (var kvp in _uiByPlayer)
        {
            var ui = kvp.Value;
            if (ui == null || ui.trail == null) continue;
            if (_frozenPlayerMapLocalById.TryGetValue(ui.id, out var mapLocal))
            {
                ui.trail.headPoint = mapLocal;
                ui.trail.hasHeadPoint = true;
            }
        }
    }

    private void UpdateBodiesFrozen()
    {
        _tmpRemoveBodies.Clear();
        foreach (var kvp in _bodyUiById)
        {
            _tmpRemoveBodies.Add(kvp.Key);
        }

        foreach (var kvp in _frozenBodyMapLocalById)
        {
            var id = kvp.Key;
            RemoveTmpBody(id);

            if (!_bodyUiById.TryGetValue(id, out var img) || img == null)
            {
                img = CreateHalo(_bodiesRoot, "Body", BodySize);
                _bodyUiById[id] = img;
            }

            var mapLocal = kvp.Value;
            if (!TryMapLocalToAnchored(mapLocal, out var anchored))
            {
                img.enabled = false;
                continue;
            }

            var s = Mathf.Clamp(scale, MinScale, MaxScale);
            var iconScale = 1f;
            try
            {
                if (MalumMenu.minimapIconScale != null) iconScale = MalumMenu.minimapIconScale.Value;
            }
            catch
            {
                iconScale = 1f;
            }
            if (iconScale < 0.50f) iconScale = 0.50f;
            if (iconScale > 2.50f) iconScale = 2.50f;

            var size = BodySize * (s / DefaultScale) * iconScale;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.anchoredPosition = anchored;
            img.color = new Color(0f, 1f, 0f, 0.85f);
            img.enabled = true;
        }

        for (var i = 0; i < _tmpRemoveBodies.Count; i++)
        {
            var id = _tmpRemoveBodies[i];
            if (_bodyUiById.TryGetValue(id, out var img) && img != null)
            {
                try { Object.Destroy(img.gameObject); } catch { }
            }
            _bodyUiById.Remove(id);
        }
    }

    private void ClearBodies()
    {
        if (_mapOverlayRoot != null)
        {
            try { Object.Destroy(_mapOverlayRoot.gameObject); } catch { }
            _mapOverlayRoot = null;
        }
        _mapBodyById.Clear();
        ClearMapPlayers();
        foreach (var kvp in _uiByPlayer)
        {
            var ui = kvp.Value;
            if (ui == null || ui.trail == null) continue;
            ui.trail.mapSegments.Clear();
        }

        foreach (var kvp in _bodyUiById)
        {
            var img = kvp.Value;
            if (img == null) continue;
            try { Object.Destroy(img.gameObject); } catch { }
        }
        _bodyUiById.Clear();
        _tmpRemoveBodies.Clear();
        _bodyMapLocalById.Clear();
        _frozenBodyMapLocalById.Clear();
        _freezeBodiesForMeeting = false;
    }

    private void ClearPlayers()
    {
        _playerMapLocalById.Clear();
        _frozenPlayerMapLocalById.Clear();
        _freezePlayersForMeeting = false;
    }

    private void RemoveTmpBody(int id)
    {
        for (var i = 0; i < _tmpRemoveBodies.Count; i++)
        {
            if (_tmpRemoveBodies[i] != id) continue;
            _tmpRemoveBodies.RemoveAt(i);
            return;
        }
    }

    private static Color SafePlayerColor(PlayerControl p)
    {
        if (p == null) return Color.white;

        try
        {
            if (p.Data != null) return p.Data.Color;
        }
        catch
        {
        }

        try
        {
            return Palette.PlayerColors[p.CurrentOutfit.ColorId];
        }
        catch
        {
        }

        return Color.white;
    }

    private void UpdateTrails()
    {
        if (_trailsRoot == null) return;

        var meeting = _wasMeeting;
        if (!CheatToggles.mapTrails || (!Utils.isPlayer && !meeting) || !Utils.isShip || ShipStatus.Instance == null)
        {
            ClearTrails();
            return;
        }

        var now = meeting ? _meetingFreezeNow : Time.time;
        var keepSeconds = MapHandler.trailSeconds;
        if (keepSeconds < 5f) keepSeconds = 5f;
        if (keepSeconds > 60f) keepSeconds = 60f;

        if (!meeting)
        {
            var players = PlayerControl.AllPlayerControls;
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p.Data == null) continue;
                    if (!_uiByPlayer.TryGetValue(p.PlayerId, out var ui) || ui == null || ui.trail == null) continue;

                    var isImp = p.Data.Role != null && p.Data.Role.IsImpostor;
                    var isDead = p.Data.IsDead;

                    var show = false;
                    if (isDead) show = CheatToggles.radarGhosts;
                    else if (isImp) show = CheatToggles.radarImps;
                    else show = CheatToggles.radarCrew;
                    if (!show) continue;

                    if (!TryWorldToMapLocal(p.transform.position, out var mapLocal)) continue;
                    ui.trail.headPoint = mapLocal;
                    ui.trail.hasHeadPoint = true;

                    if (now < ui.trail.nextRecordTime) continue;
                    ui.trail.nextRecordTime = now + TrailWaypointIntervalSeconds;

                    AddTrailPoint(ui.trail, mapLocal, now);
                }
            }
        }

        foreach (var kvp in _uiByPlayer)
        {
            var ui = kvp.Value;
            if (ui == null || ui.trail == null) continue;
            if (!meeting) TrimTrail(ui.trail, now, keepSeconds);
            RenderTrail(ui.trail, now, keepSeconds);
        }
    }

    private static void AddTrailPoint(object trailObj, Vector2 pos, float now)
    {
        var trail = trailObj as RadarTrail;
        if (trail == null) return;

        if (trail.count > 0)
        {
            var lastIndex = (trail.start + trail.count - 1) % TrailMaxWaypoints;
            var lastPos = trail.points[lastIndex];
            if (Vector2.Distance(lastPos, pos) < 0.02f) return;
        }

        var idx = (trail.start + trail.count) % TrailMaxWaypoints;
        if (trail.count == TrailMaxWaypoints)
        {
            trail.start = (trail.start + 1) % TrailMaxWaypoints;
            idx = (trail.start + trail.count - 1) % TrailMaxWaypoints;
            trail.points[idx] = pos;
            trail.times[idx] = now;
        }
        else
        {
            trail.points[idx] = pos;
            trail.times[idx] = now;
            trail.count++;
        }
    }

    private static void TrimTrail(object trailObj, float now, float keepSeconds)
    {
        var trail = trailObj as RadarTrail;
        if (trail == null) return;

        if (keepSeconds <= 0.001f) return;

        while (trail.count > 0)
        {
            var idx = trail.start;
            var age = now - trail.times[idx];
            if (age <= keepSeconds) break;
            trail.start = (trail.start + 1) % TrailMaxWaypoints;
            trail.count--;
        }
    }

    private void RenderTrail(object trailObj, float now, float keepSeconds)
    {
        var trail = trailObj as RadarTrail;
        if (trail == null) return;

        var count = trail.count;
        if (count <= 0)
        {
            HideAllSegments(trail);
            return;
        }

        var rawSegments = trail.hasHeadPoint ? count : (count - 1);
        if (rawSegments <= 0)
        {
            HideAllSegments(trail);
            return;
        }

        var needed = rawSegments;
        if (needed > TrailMaxSegments) needed = TrailMaxSegments;
        while (trail.segments.Count < needed)
        {
            var seg = CreateSegment();
            trail.segments.Add(seg);
        }

        for (var i = 0; i < trail.segments.Count; i++)
        {
            trail.segments[i].enabled = i < needed;
        }

        var newestIdx = (trail.start + trail.count - 1) % TrailMaxWaypoints;
        var fromLocal = trail.hasHeadPoint ? trail.headPoint : trail.points[newestIdx];
        var fromTime = trail.hasHeadPoint ? now : trail.times[newestIdx];
        var idx = newestIdx;
        var remainingLinks = trail.hasHeadPoint ? trail.count : (trail.count - 1);

        for (var i = 0; i < needed; i++)
        {
            if (remainingLinks <= 0)
            {
                trail.segments[i].enabled = false;
                continue;
            }

            Vector2 toLocal;
            float toTime;
            if (trail.hasHeadPoint && i == 0)
            {
                toLocal = trail.points[newestIdx];
                toTime = trail.times[newestIdx];
            }
            else
            {
                idx = (idx - 1 + TrailMaxWaypoints) % TrailMaxWaypoints;
                toLocal = trail.points[idx];
                toTime = trail.times[idx];
            }
            remainingLinks--;

            if (!TryMapLocalToAnchored(fromLocal, out var pa))
            {
                trail.segments[i].enabled = false;
                fromLocal = toLocal;
                fromTime = toTime;
                continue;
            }
            if (!TryMapLocalToAnchored(toLocal, out var pb))
            {
                trail.segments[i].enabled = false;
                fromLocal = toLocal;
                fromTime = toTime;
                continue;
            }

            var d = pb - pa;
            var len = d.magnitude;
            if (len < 0.001f)
            {
                trail.segments[i].enabled = false;
                fromLocal = toLocal;
                fromTime = toTime;
                continue;
            }

            var mid = (pa + pb) * 0.5f;
            var angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            var seg = trail.segments[i];
            var age = now - toTime;
            var t = keepSeconds > 0.001f ? Mathf.Clamp01(age / keepSeconds) : 1f;
            var alpha = ResolveTrailAlpha(t);
            seg.color = new Color(trail.color.r, trail.color.g, trail.color.b, alpha);
            var rt = seg.rectTransform;
            rt.anchoredPosition = mid;
            rt.sizeDelta = new Vector2(len, TrailWidth);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            seg.enabled = true;

            fromLocal = toLocal;
            fromTime = toTime;
        }
    }

    private Image CreateSegment()
    {
        var go = new GameObject("Seg");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_trailsRoot, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(10f, TrailWidth);

        var img = go.AddComponent<Image>();
        img.sprite = GetFallbackSprite();
        img.raycastTarget = false;
        img.enabled = false;
        return img;
    }

    private static void HideAllSegments(object trailObj)
    {
        var trail = trailObj as RadarTrail;
        if (trail == null) return;

        for (var i = 0; i < trail.segments.Count; i++)
        {
            if (trail.segments[i] != null) trail.segments[i].enabled = false;
        }
    }

    private void ClearTrails()
    {
        foreach (var kvp in _uiByPlayer)
        {
            var ui = kvp.Value;
            if (ui == null || ui.trail == null) continue;

            ui.trail.start = 0;
            ui.trail.count = 0;
            ui.trail.nextRecordTime = 0f;
            ui.trail.hasHeadPoint = false;

            HideAllSegments(ui.trail);
            DisableBigMapTrail(ui.trail);
        }
    }

    private void MaybeSaveWindow()
    {
        var now = Time.unscaledTime;
        if (now < _nextSaveTime) return;
        _nextSaveTime = now + 0.25f;

        var s = Mathf.Clamp(scale, MinScale, MaxScale);
        var pos = anchoredPosition;

        var posChanged = Vector2.Distance(pos, _lastSavedAnchored) > 0.01f;
        var scaleChanged = Mathf.Abs(s - _lastSavedScale) > 0.0001f;
        if (!posChanged && !scaleChanged) return;

        _lastSavedAnchored = pos;
        _lastSavedScale = s;

        if (MalumMenu.Plugin == null) return;

        try
        {
            MalumMenu.minimapScale.Value = s;
            MalumMenu.minimapPosX.Value = pos.x;
            MalumMenu.minimapPosY.Value = pos.y;
            MalumMenu.Plugin.Config.Save();
        }
        catch
        {
        }
    }

    private void SaveDebugInfo()
    {
        Texture2D readableTex = null;

        try
        {
            var amongUsPath = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(amongUsPath)) return;

            var debugFolder = Path.Combine(amongUsPath, "MalumMenu_Debug");
            Directory.CreateDirectory(debugFolder);

            // Save map texture if available (from game renderer)
            if (_bgRenderer != null && _bgRenderer.sprite != null && _bgRenderer.sprite.texture != null)
            {
                var originalTex = _bgRenderer.sprite.texture;

                try
                {
                    // Try to make texture readable by creating a copy
                    readableTex = new Texture2D(originalTex.width, originalTex.height, TextureFormat.RGBA32, false);
                    readableTex.name = originalTex.name + "_readable";

                    // Use RenderTexture to copy the texture
                    var tempRT = RenderTexture.GetTemporary(originalTex.width, originalTex.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(originalTex, tempRT);

                    // Read from RenderTexture
                    RenderTexture.active = tempRT;
                    readableTex.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                    readableTex.Apply();

                    RenderTexture.ReleaseTemporary(tempRT);
                    RenderTexture.active = null;

                    // Save the readable texture as PNG
                    var pngPath = Path.Combine(debugFolder, $"radar_map_texture_{originalTex.name}.png");
                    File.WriteAllBytes(pngPath, readableTex.EncodeToPNG());
                    MalumMenu.Log?.LogInfo($"Texture saved to: {pngPath}");
                }
                catch (Exception texEx)
                {
                    MalumMenu.Log?.LogWarning($"Could not save texture image: {texEx.Message}");
                }

                // Save parameters in map-specific file
                var sb = new StringBuilder();
                sb.AppendLine("Radar Map Debug Info");
                sb.AppendLine($"Saved: {DateTime.Now}");
                sb.AppendLine();
                sb.AppendLine($"Texture Name: {_bgRenderer.sprite.name}");
                sb.AppendLine($"Texture Size: {originalTex.width}x{originalTex.height}");
                sb.AppendLine($"Sprite Bounds: {_bgRenderer.sprite.bounds}");
                sb.AppendLine($"Sprite PixelsPerUnit: {_bgRenderer.sprite.pixelsPerUnit}");
                sb.AppendLine($"Renderer Scale: {_bgRenderer.transform.lossyScale}");
                sb.AppendLine($"MapSpace: {_mapSpace?.name}");
                sb.AppendLine($"Scale: {scale}");
                sb.AppendLine($"Position: {anchoredPosition}");
                sb.AppendLine($"Bounds Size: {_bgBoundsSize}");
                sb.AppendLine($"Bounds Min: {_bgBoundsMin}");
                sb.AppendLine($"UV Rect: {_bgUv}");

                var mapName = _bgRenderer.sprite.name.Replace(" ", "_");
                var txtPath = Path.Combine(debugFolder, $"radar_map_params_{mapName}.txt");
                File.WriteAllText(txtPath, sb.ToString());

                MalumMenu.Log?.LogInfo($"Debug info saved to: {debugFolder}");
            }

            // Save crewmate icon if available (the sprite used for radar icons)
            if (_iconSprite != null && _iconSprite.texture != null)
            {
                SaveCrewmateIcon();
            }
        }
        catch (Exception ex)
        {
            MalumMenu.Log?.LogError($"Failed to save debug info: {ex.Message}");
        }
        finally
        {
            if (readableTex != null) Object.Destroy(readableTex);
        }
    }

    private void SaveCrewmateIcon()
    {
        if (_iconSprite == null || _iconSprite.texture == null) return;

        Texture2D readableIcon = null;
        RenderTexture tempRT = null;
        RenderTexture prevActive = null;

        try
        {
            var amongUsPath = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(amongUsPath)) return;

            var debugFolder = Path.Combine(amongUsPath, "MalumMenu_Debug");
            Directory.CreateDirectory(debugFolder);

            var iconTex = _iconSprite.texture;

            // --- KEY FIX: blit the WHOLE atlas into a readable RT first ---
            tempRT = RenderTexture.GetTemporary(
                iconTex.width, iconTex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(iconTex, tempRT);

            prevActive = RenderTexture.active;
            RenderTexture.active = tempRT;

            // Read the full atlas into a readable Texture2D
            var fullAtlas = new Texture2D(iconTex.width, iconTex.height, TextureFormat.RGBA32, false);
            fullAtlas.ReadPixels(new Rect(0, 0, iconTex.width, iconTex.height), 0, 0);
            fullAtlas.Apply();

            RenderTexture.active = prevActive;
            prevActive = null;
            RenderTexture.ReleaseTemporary(tempRT);
            tempRT = null;

            // --- Crop out just the sprite's rect from the atlas ---
            // textureRect is in pixels, bottom-left origin (Unity convention)
            var tr = _iconSprite.textureRect;
            int x      = Mathf.RoundToInt(tr.x);
            int y      = Mathf.RoundToInt(tr.y);
            int width  = Mathf.RoundToInt(tr.width);
            int height = Mathf.RoundToInt(tr.height);

            // Clamp to atlas bounds defensively
            x      = Mathf.Clamp(x,     0, iconTex.width  - 1);
            y      = Mathf.Clamp(y,     0, iconTex.height - 1);
            width  = Mathf.Clamp(width,  1, iconTex.width  - x);
            height = Mathf.Clamp(height, 1, iconTex.height - y);

            readableIcon = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // GetPixels uses bottom-left origin, same as textureRect — no flip needed
            var pixels = fullAtlas.GetPixels(x, y, width, height);
            readableIcon.SetPixels(pixels);
            readableIcon.Apply();

            Object.Destroy(fullAtlas); // free the full-atlas copy ASAP

            var iconPath = Path.Combine(debugFolder, "radar_crewmate_icon.png");
            File.WriteAllBytes(iconPath, readableIcon.EncodeToPNG());
            MalumMenu.Log?.LogInfo(
                $"Crewmate icon saved: {iconPath}  " +
                $"atlasRect=({x},{y},{width},{height})  " +
                $"atlasSize={iconTex.width}x{iconTex.height}");
        }
        catch (Exception iconEx)
        {
            MalumMenu.Log?.LogWarning($"Could not save crewmate icon: {iconEx.Message}");
        }
        finally
        {
            if (prevActive != null) RenderTexture.active = prevActive;
            if (tempRT    != null) RenderTexture.ReleaseTemporary(tempRT);
            if (readableIcon != null) Object.Destroy(readableIcon);
        }
    }

    private void SaveCalibrationData()
    {
        try
        {
            if (!Utils.isShip || ShipStatus.Instance == null) return;

            var amongUsPath = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(amongUsPath)) return;

            var debugFolder = Path.Combine(amongUsPath, "MalumMenu_Debug");
            Directory.CreateDirectory(debugFolder);

            var sb = new StringBuilder();
            sb.AppendLine("=== RADAR CALIBRATION DATA ===");
            sb.AppendLine($"Saved: {DateTime.Now}");
            sb.AppendLine($"MapScale: {ShipStatus.Instance.MapScale}");
            sb.AppendLine($"ShipTransform.localScale: {ShipStatus.Instance.transform.localScale}");
            sb.AppendLine($"MapFlipX sign: {Mathf.Sign(ShipStatus.Instance.transform.localScale.x)}");
            sb.AppendLine();

            // Current bounds being used
            sb.AppendLine($"Current _bgBoundsMin: {_bgBoundsMin}");
            sb.AppendLine($"Current _bgBoundsSize: {_bgBoundsSize}");
            sb.AppendLine($"Current _contentAspect: {_contentAspect}");
            sb.AppendLine();

            // If we have a background renderer, get its actual bounds
            if (_bgRenderer != null && _bgRenderer.sprite != null)
            {
                var bounds = _bgRenderer.sprite.bounds;
                var lossyScale = _bgRenderer.transform.lossyScale;
                sb.AppendLine("=== FROM BGRENDERER ===");
                sb.AppendLine($"bgRenderer.sprite.bounds.min: {bounds.min}");
                sb.AppendLine($"bgRenderer.sprite.bounds.size: {bounds.size}");
                sb.AppendLine($"bgRenderer.transform.lossyScale: {lossyScale}");
                sb.AppendLine($"bgRenderer.transform.position: {_bgRenderer.transform.position}");
                sb.AppendLine($"bgRenderer.transform.localPosition: {_bgRenderer.transform.localPosition}");

                // World bounds of the background sprite
                var worldMin = _bgRenderer.transform.TransformPoint(bounds.min);
                var worldMax = _bgRenderer.transform.TransformPoint(bounds.max);
                sb.AppendLine($"World bounds min: {worldMin}");
                sb.AppendLine($"World bounds max: {worldMax}");

                // Convert to map-local coords
                if (_mapSpace != null)
                {
                    var mapMin = _mapSpace.InverseTransformPoint(worldMin);
                    var mapMax = _mapSpace.InverseTransformPoint(worldMax);
                    sb.AppendLine($"MapLocal bounds min: {mapMin}");
                    sb.AppendLine($"MapLocal bounds max: {mapMax}");
                }
            }
            sb.AppendLine();

            // Sample all players — compare world pos → mapLocal → what we compute
            sb.AppendLine("=== PLAYER POSITION SAMPLES ===");
            var players = PlayerControl.AllPlayerControls;
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p.Data == null) continue;

                    var worldPos = p.transform.position;

                    // Our current TryWorldToMapLocal
                    TryWorldToMapLocal(worldPos, out var ourMapLocal);

                    // Direct from ShipStatus
                    var rawMapLocal = new Vector2(
                        worldPos.x / ShipStatus.Instance.MapScale * Mathf.Sign(ShipStatus.Instance.transform.localScale.x),
                        worldPos.y / ShipStatus.Instance.MapScale
                    );

                    // What UV does this produce with current bounds?
                    var u = (ourMapLocal.x - _bgBoundsMin.x) / _bgBoundsSize.x;
                    var v = (ourMapLocal.y - _bgBoundsMin.y) / _bgBoundsSize.y;

                    sb.AppendLine($"Player {p.PlayerId} ({p.Data.PlayerName}):");
                    sb.AppendLine($"  WorldPos:    ({worldPos.x:F4}, {worldPos.y:F4})");
                    sb.AppendLine($"  OurMapLocal: ({ourMapLocal.x:F4}, {ourMapLocal.y:F4})");
                    sb.AppendLine($"  RawMapLocal: ({rawMapLocal.x:F4}, {rawMapLocal.y:F4})");
                    sb.AppendLine($"  UV:          ({u:F4}, {v:F4})");
                    sb.AppendLine($"  UV in range: {(u >= 0 && u <= 1 && v >= 0 && v <= 1)}");
                }
            }
            sb.AppendLine();

            // Now compute what the bounds SHOULD be from the actual game map renderer
            sb.AppendLine("=== SUGGESTED BOUNDS FROM LIVE DATA ===");
            if (_bgRenderer != null && _bgRenderer.sprite != null && _mapSpace != null)
            {
                // Get the sprite bounds in map-space coordinates
                var spriteBounds = _bgRenderer.sprite.bounds;
                var corners = new Vector3[]
                {
                    new Vector3(spriteBounds.min.x, spriteBounds.min.y, 0),
                    new Vector3(spriteBounds.max.x, spriteBounds.min.y, 0),
                    new Vector3(spriteBounds.min.x, spriteBounds.max.y, 0),
                    new Vector3(spriteBounds.max.x, spriteBounds.max.y, 0),
                };

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                foreach (var corner in corners)
                {
                    var world = _bgRenderer.transform.TransformPoint(corner);
                    var mapLocal = _mapSpace.InverseTransformPoint(world);
                    minX = Mathf.Min(minX, mapLocal.x);
                    minY = Mathf.Min(minY, mapLocal.y);
                    maxX = Mathf.Max(maxX, mapLocal.x);
                    maxY = Mathf.Max(maxY, mapLocal.y);
                }

                sb.AppendLine($"BoundsMin: ({minX:F4}, {minY:F4})");
                sb.AppendLine($"BoundsSize: ({maxX - minX:F4}, {maxY - minY:F4})");
                sb.AppendLine($"// RadarMapConfig entry:");
                sb.AppendLine($"// new RadarMapConfig(\"map\", new Vector2({minX:F4}f, {minY:F4}f), new Vector2({maxX - minX:F4}f, {maxY - minY:F4}f))");
            }
            else
            {
                sb.AppendLine("bgRenderer not available — using player positions to estimate bounds");

                // Estimate from player positions (less accurate but useful)
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                bool any = false;

                if (players != null)
                {
                    for (var i = 0; i < players.Count; i++)
                    {
                        var p = players[i];
                        if (p == null) continue;
                        TryWorldToMapLocal(p.transform.position, out var ml);
                        minX = Mathf.Min(minX, ml.x);
                        minY = Mathf.Min(minY, ml.y);
                        maxX = Mathf.Max(maxX, ml.x);
                        maxY = Mathf.Max(maxY, ml.y);
                        any = true;
                    }
                }

                if (any)
                {
                    // Add 20% padding since players don't cover the whole map
                    var padX = (maxX - minX) * 0.2f;
                    var padY = (maxY - minY) * 0.2f;
                    minX -= padX; minY -= padY;
                    maxX += padX; maxY += padY;
                    sb.AppendLine($"Estimated BoundsMin: ({minX:F4}, {minY:F4})");
                    sb.AppendLine($"Estimated BoundsSize: ({maxX - minX:F4}, {maxY - minY:F4})");
                }
            }

            var path = Path.Combine(debugFolder, "radar_calibration.txt");
            File.WriteAllText(path, sb.ToString());
            MalumMenu.Log?.LogInfo($"Calibration data saved to: {path}");
        }
        catch (Exception ex)
        {
            MalumMenu.Log?.LogError($"SaveCalibrationData failed: {ex.Message}");
        }
    }

    private void LogMapCalibration(MapBehaviour map)
    {
        if (map == null || MalumMenu.Log == null) return;
        try
        {
            var log = MalumMenu.Log;
            log.LogInfo("=== MAP CALIBRATION DUMP ===");

            // ShipStatus
            if (ShipStatus.Instance != null)
            {
                var ship = ShipStatus.Instance;
                log.LogInfo($"ShipStatus.MapScale={ship.MapScale}");
                log.LogInfo($"ShipStatus.transform.localScale={ship.transform.localScale}");
                log.LogInfo($"ShipStatus.transform.position={ship.transform.position}");
            }

            // MapBehaviour root
            log.LogInfo($"MapBehaviour.name={map.name}");
            log.LogInfo($"MapBehaviour.transform.position={map.transform.position}");
            log.LogInfo($"MapBehaviour.transform.localScale={map.transform.localScale}");
            log.LogInfo($"MapBehaviour.transform.lossyScale={map.transform.lossyScale}");

            // HerePoint / mapSpace
            var here = map.HerePoint;
            if (here != null)
            {
                log.LogInfo($"HerePoint.name={here.gameObject.name}");
                log.LogInfo($"HerePoint.transform.position={here.transform.position}");
                log.LogInfo($"HerePoint.transform.localPosition={here.transform.localPosition}");
                log.LogInfo($"HerePoint.transform.parent={here.transform.parent?.name ?? "null"}");
                log.LogInfo($"HerePoint.sprite={here.sprite?.name ?? "null"}");
            }
            else
            {
                log.LogInfo("HerePoint=null");
            }

            // _mapSpace
            log.LogInfo($"_mapSpace={_mapSpace?.name ?? "null"}");
            if (_mapSpace != null)
            {
                log.LogInfo($"_mapSpace.position={_mapSpace.position}");
                log.LogInfo($"_mapSpace.localScale={_mapSpace.localScale}");
                log.LogInfo($"_mapSpace.lossyScale={_mapSpace.lossyScale}");
            }

            // Dump ALL SpriteRenderers under MapBehaviour
            log.LogInfo("--- SpriteRenderers under MapBehaviour ---");
            var renderers = map.GetComponentsInChildren<SpriteRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var s = r.sprite;
                var texSz = s?.texture != null ? $"{s.texture.width}x{s.texture.height}" : "noTex";
                var px = s != null ? (s.textureRect.width * s.textureRect.height).ToString("0") : "0";
                var wb = s?.bounds.size ?? Vector3.zero;
                var ls = r.transform.lossyScale;
                var worldArea = (wb.x * Mathf.Abs(ls.x)) * (wb.y * Mathf.Abs(ls.y));
                var pos3 = r.transform.position;
                log.LogInfo($"  [{i}] go={r.gameObject.name} sprite={s?.name ?? "null"} tex={texSz} px={px} " +
                            $"bounds=({wb.x:F3},{wb.y:F3},{wb.z:F3}) lossyScale=({ls.x:F3},{ls.y:F3},{ls.z:F3}) worldArea={worldArea:F3} " +
                            $"order={r.sortingOrder} pos=({pos3.x:F3},{pos3.y:F3},{pos3.z:F3}) active={r.gameObject.activeInHierarchy}");
            }

            // _bgRenderer result
            if (_bgRenderer != null && _bgRenderer.sprite != null)
            {
                var b = _bgRenderer.sprite.bounds;
                log.LogInfo($"_bgRenderer='{_bgRenderer.gameObject.name}' bounds.min=({b.min.x:F4},{b.min.y:F4}) bounds.size=({b.size.x:F4},{b.size.y:F4})");
                if (_mapSpace != null)
                {
                    var wMin = _bgRenderer.transform.TransformPoint(b.min);
                    var wMax = _bgRenderer.transform.TransformPoint(b.max);
                    var mMin = _mapSpace.InverseTransformPoint(wMin);
                    var mMax = _mapSpace.InverseTransformPoint(wMax);
                    log.LogInfo($"  -> mapLocal min=({mMin.x:F4},{mMin.y:F4}) max=({mMax.x:F4},{mMax.y:F4})");
                    log.LogInfo($"  -> SUGGESTED BoundsMin=({mMin.x:F4}f, {mMin.y:F4}f) BoundsSize=({mMax.x-mMin.x:F4}f, {mMax.y-mMin.y:F4}f)");
                }
            }
            else
            {
                log.LogInfo("_bgRenderer=null — no game renderer found");
            }

            // Current config bounds
            log.LogInfo($"_bgBoundsMin=({_bgBoundsMin.x:F4},{_bgBoundsMin.y:F4}) _bgBoundsSize=({_bgBoundsSize.x:F4},{_bgBoundsSize.y:F4})");

            // Player world → mapLocal
            log.LogInfo("--- Players world->mapLocal ---");
            var players = PlayerControl.AllPlayerControls;
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p.Data == null) continue;
                    var wp = p.transform.position;
                    TryWorldToMapLocal(wp, out var ml);
                    var u = _bgBoundsSize.x > 0 ? (ml.x - _bgBoundsMin.x) / _bgBoundsSize.x : -1f;
                    var v = _bgBoundsSize.y > 0 ? (ml.y - _bgBoundsMin.y) / _bgBoundsSize.y : -1f;
                    log.LogInfo($"  P{p.PlayerId}({p.Data.PlayerName}) world=({wp.x:F2},{wp.y:F2}) mapLocal=({ml.x:F2},{ml.y:F2}) uv=({u:F2},{v:F2})");
                }
            }

            log.LogInfo("=== END CALIBRATION DUMP ===");
        }
        catch (Exception ex)
        {
            MalumMenu.Log?.LogWarning($"LogMapCalibration failed: {ex.Message}");
        }
    }
}
