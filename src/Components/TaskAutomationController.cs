using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MalumMenu;

public class TaskAutomationController : MonoBehaviour
{
    private static readonly Dictionary<Type, Func<Minigame, PlayerTask>> TaskGetterByType = new Dictionary<Type, Func<Minigame, PlayerTask>>(64);
    private static readonly HashSet<Type> LoggedMissingTask = new HashSet<Type>();
    private static readonly HashSet<Type> LoggedIgnoredNotInGame = new HashSet<Type>();
    private static readonly Dictionary<Type, string> TaskGetterDescByType = new Dictionary<Type, string>(64);
    private static readonly HashSet<Type> LoggedGetterInfo = new HashSet<Type>();
    private static readonly Dictionary<Type, Func<object, PlayerTask>> TaskExtractorByType = new Dictionary<Type, Func<object, PlayerTask>>(64);
    private static readonly Dictionary<Type, string> TaskExtractorDescByType = new Dictionary<Type, string>(64);

    private Minigame _minigame;
    private PlayerTask _task;
    private bool _active;
    private bool _auto;
    private bool _closingFromAuto;
    private float _start;
    private float _end;
    private float _duration;
    private int _mapId;
    private int _taskType;
    private int _taskId;
    private Minigame _lastSeenMinigame;
    private float _nextScanTime;
    private float _nextCompleteAttemptTime;
    private int _completeAttempts;
    private bool _waitingForCompleteAck;
    private float _completeAckDeadline;
    private Canvas _canvas;
    private CanvasScaler _scaler;
    private RectTransform _barRoot;
    private Image _barFill;

    private static bool ShouldRecordTimes()
    {
        return CheatToggles.recordTaskTimes || CheatToggles.autoTaskUseBestTime;
    }

    private void EnsureBarUi()
    {
        if (_canvas != null) return;

        var root = new GameObject("MalumTaskAutoCanvas");
        Object.DontDestroyOnLoad(root);

        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1001;

        _scaler = root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920f, 1080f);

        root.AddComponent<GraphicRaycaster>();

        var barGo = new GameObject("TaskAutoBar");
        _barRoot = barGo.AddComponent<RectTransform>();
        _barRoot.SetParent(_canvas.transform, false);
        _barRoot.anchorMin = new Vector2(0.5f, 0f);
        _barRoot.anchorMax = new Vector2(0.5f, 0f);
        _barRoot.pivot = new Vector2(0.5f, 0.5f);
        _barRoot.anchoredPosition = new Vector2(0f, 42f);
        _barRoot.sizeDelta = new Vector2(420f, 14f);

        var bg = barGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);
        bg.raycastTarget = false;

        var fillGo = new GameObject("Fill");
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.SetParent(_barRoot, false);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.pivot = new Vector2(0.5f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(-4f, -4f);

        _barFill = fillGo.AddComponent<Image>();
        _barFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _barFill.type = Image.Type.Filled;
        _barFill.fillMethod = Image.FillMethod.Horizontal;
        _barFill.fillOrigin = 0;
        _barFill.fillAmount = 0f;
        _barFill.color = new Color(0.2f, 0.95f, 0.2f, 0.85f);
        _barFill.raycastTarget = false;

        _canvas.gameObject.SetActive(false);
    }

    private void SetBarVisible(bool visible)
    {
        if (_canvas == null) return;
        if (_canvas.gameObject.activeSelf == visible) return;
        _canvas.gameObject.SetActive(visible);
    }

    private void UpdateBar(float now)
    {
        if (_barFill == null) return;
        var dur = _end - _start;
        if (dur <= 0.001f)
        {
            _barFill.fillAmount = 0f;
            return;
        }
        var t = Mathf.Clamp01((now - _start) / dur);
        _barFill.fillAmount = t;
    }

    private static bool TryGetTaskFromMinigame(Minigame minigame, out PlayerTask task)
    {
        task = null;
        if (minigame == null) return false;

        var t = minigame.GetType();
        if (t == null) return false;

        if (!TaskGetterByType.TryGetValue(t, out var getter) || getter == null)
        {
            getter = CreateTaskGetterForType(t, out var desc);
            TaskGetterByType[t] = getter;
            TaskGetterDescByType[t] = desc;
        }

        if (getter == null) return false;

        try
        {
            task = getter(minigame);
        }
        catch
        {
            task = null;
        }

        return task != null;
    }

    private static Func<Minigame, PlayerTask> CreateTaskGetterForType(Type minigameType, out string desc)
    {
        desc = null;
        if (minigameType == null) return null;

        var getter = TryCreateGetterFromMembers(minigameType, out desc);
        if (getter != null) return getter;

        if (minigameType != typeof(Minigame))
        {
            getter = TryCreateGetterFromMembers(typeof(Minigame), out desc);
            if (getter != null) return getter;
        }

        getter = TryCreateGetterFromTypedMembers(minigameType, out desc);
        if (getter != null) return getter;

        if (minigameType != typeof(Minigame))
        {
            getter = TryCreateGetterFromTypedMembers(typeof(Minigame), out desc);
            if (getter != null) return getter;
        }

        getter = TryCreateGetterFromTaskishMembers(minigameType, out desc);
        if (getter != null) return getter;

        if (minigameType != typeof(Minigame))
        {
            getter = TryCreateGetterFromTaskishMembers(typeof(Minigame), out desc);
            if (getter != null) return getter;
        }

        if (CheatToggles.debugTaskAutomation)
        {
            MalumMenu.Log.LogInfo($"TaskAutomation: no task getter for minigameType={minigameType.Name}");
        }
        return null;
    }

    private static Func<Minigame, PlayerTask> TryCreateGetterFromMembers(Type type, out string desc)
    {
        desc = null;
        var propNames = new[] { "MyTask", "myTask", "Task", "task" };
        for (var i = 0; i < propNames.Length; i++)
        {
            var pi = AccessTools.Property(type, propNames[i]);
            if (pi != null)
            {
                desc = $"{type.Name}.{propNames[i]} (property)";
                return mg =>
                {
                    try { return pi.GetValue(mg, null) as PlayerTask; } catch { return null; }
                };
            }

            var getter = AccessTools.Method(type, "get_" + propNames[i]);
            if (getter != null)
            {
                desc = $"{type.Name}.get_{propNames[i]}()";
                return mg =>
                {
                    try { return getter.Invoke(mg, null) as PlayerTask; } catch { return null; }
                };
            }
        }

        var fieldNames = new[] { "MyTask", "myTask", "Task", "task" };
        for (var i = 0; i < fieldNames.Length; i++)
        {
            var field = AccessTools.Field(type, fieldNames[i]);
            if (field != null)
            {
                desc = $"{type.Name}.{fieldNames[i]} (field)";
                return mg =>
                {
                    try { return field.GetValue(mg) as PlayerTask; } catch { return null; }
                };
            }
        }

        return null;
    }

    private static Func<Minigame, PlayerTask> TryCreateGetterFromTypedMembers(Type type, out string desc)
    {
        desc = null;
        if (type == null) return null;

        var cur = type;
        while (cur != null)
        {
            try
            {
                var props = AccessTools.GetDeclaredProperties(cur);
                if (props != null)
                {
                    for (var i = 0; i < props.Count; i++)
                    {
                        var p = props[i];
                        if (p == null) continue;
                        if (p.GetIndexParameters().Length != 0) continue;
                        if (p.PropertyType == null) continue;
                        if (!typeof(PlayerTask).IsAssignableFrom(p.PropertyType)) continue;
                        desc = $"{cur.Name}.{p.Name} (typed property)";
                        return mg =>
                        {
                            try { return p.GetValue(mg, null) as PlayerTask; } catch { return null; }
                        };
                    }
                }
            }
            catch
            {
            }

            try
            {
                var fields = AccessTools.GetDeclaredFields(cur);
                if (fields != null)
                {
                    for (var i = 0; i < fields.Count; i++)
                    {
                        var f = fields[i];
                        if (f == null) continue;
                        if (f.FieldType == null) continue;
                        if (!typeof(PlayerTask).IsAssignableFrom(f.FieldType)) continue;
                        desc = $"{cur.Name}.{f.Name} (typed field)";
                        return mg =>
                        {
                            try { return f.GetValue(mg) as PlayerTask; } catch { return null; }
                        };
                    }
                }
            }
            catch
            {
            }

            if (cur == typeof(Minigame)) break;
            cur = cur.BaseType;
        }

        return null;
    }

    private static bool IsTaskishName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.IndexOf("task", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (string.Equals(name, "console", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static Func<Minigame, PlayerTask> TryCreateGetterFromTaskishMembers(Type type, out string desc)
    {
        desc = null;
        if (type == null) return null;

        List<FieldInfo> fields = null;
        List<PropertyInfo> props = null;
        try { fields = AccessTools.GetDeclaredFields(type); } catch { fields = null; }
        try { props = AccessTools.GetDeclaredProperties(type); } catch { props = null; }

        var hasAny = false;
        if (fields != null)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (f == null) continue;
                if (!IsTaskishName(f.Name)) continue;
                hasAny = true;
                break;
            }
        }
        if (!hasAny && props != null)
        {
            for (var i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (!IsTaskishName(p.Name)) continue;
                hasAny = true;
                break;
            }
        }

        if (!hasAny) return null;
        desc = $"{type.Name}.* (taskish members)";

        return mg =>
        {
            if (mg == null) return null;

            if (fields != null)
            {
                for (var i = 0; i < fields.Count; i++)
                {
                    var f = fields[i];
                    if (f == null) continue;
                    if (!IsTaskishName(f.Name)) continue;

                    object v = null;
                    try { v = f.GetValue(mg); } catch { v = null; }
                    var pt = TryResolveTaskValue(v);
                    if (pt != null) return pt;
                }
            }

            if (props != null)
            {
                for (var i = 0; i < props.Count; i++)
                {
                    var p = props[i];
                    if (p == null) continue;
                    if (p.GetIndexParameters().Length != 0) continue;
                    if (!IsTaskishName(p.Name)) continue;

                    object v = null;
                    try { v = p.GetValue(mg, null); } catch { v = null; }
                    var pt = TryResolveTaskValue(v);
                    if (pt != null) return pt;
                }
            }

            return null;
        };
    }

    private static PlayerTask TryResolveTaskValue(object value)
    {
        if (value == null) return null;
        if (value is PlayerTask pt) return pt;

        var t = value.GetType();
        if (t == null) return null;

        if (!TaskExtractorByType.TryGetValue(t, out var extractor))
        {
            extractor = CreateTaskExtractorForType(t, out var desc);
            TaskExtractorByType[t] = extractor;
            TaskExtractorDescByType[t] = desc;
        }

        if (extractor == null) return null;
        try { return extractor(value); } catch { return null; }
    }

    private static Func<object, PlayerTask> CreateTaskExtractorForType(Type type, out string desc)
    {
        desc = null;
        if (type == null) return null;

        List<FieldInfo> fields = null;
        List<PropertyInfo> props = null;
        try { fields = AccessTools.GetDeclaredFields(type); } catch { fields = null; }
        try { props = AccessTools.GetDeclaredProperties(type); } catch { props = null; }

        if (props != null)
        {
            for (var i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (p.PropertyType != null && typeof(PlayerTask).IsAssignableFrom(p.PropertyType))
                {
                    desc = $"{type.Name}.{p.Name} (extract typed property)";
                    return o =>
                    {
                        try { return p.GetValue(o, null) as PlayerTask; } catch { return null; }
                    };
                }
            }
        }

        if (fields != null)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (f == null) continue;
                if (f.FieldType != null && typeof(PlayerTask).IsAssignableFrom(f.FieldType))
                {
                    desc = $"{type.Name}.{f.Name} (extract typed field)";
                    return o =>
                    {
                        try { return f.GetValue(o) as PlayerTask; } catch { return null; }
                    };
                }
            }
        }

        if (props != null)
        {
            for (var i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (!IsTaskishName(p.Name)) continue;
                desc = $"{type.Name}.{p.Name} (extract taskish property)";
                return o =>
                {
                    object v = null;
                    try { v = p.GetValue(o, null); } catch { v = null; }
                    return v as PlayerTask;
                };
            }
        }

        if (fields != null)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (f == null) continue;
                if (!IsTaskishName(f.Name)) continue;
                desc = $"{type.Name}.{f.Name} (extract taskish field)";
                return o =>
                {
                    object v = null;
                    try { v = f.GetValue(o); } catch { v = null; }
                    return v as PlayerTask;
                };
            }
        }

        return null;
    }

    private static void LogMinigameTaskDiagnostics(Type minigameType)
    {
        if (minigameType == null) return;
        if (!CheatToggles.debugTaskAutomation) return;

        try
        {
            var fields = AccessTools.GetDeclaredFields(minigameType);
            var props = AccessTools.GetDeclaredProperties(minigameType);

            var msg = "TaskAutomation: task diagnostics minigameType=" + minigameType.Name + " fields=";
            var added = 0;

            if (fields != null)
            {
                for (var i = 0; i < fields.Count && added < 10; i++)
                {
                    var f = fields[i];
                    if (f == null) continue;
                    var taskish = IsTaskishName(f.Name);
                    var typed = f.FieldType != null && typeof(PlayerTask).IsAssignableFrom(f.FieldType);
                    if (!taskish && !typed) continue;
                    msg += (added == 0 ? "" : ", ") + f.Name + ":" + (f.FieldType != null ? f.FieldType.Name : "null");
                    added++;
                }
            }

            msg += " props=";
            added = 0;

            if (props != null)
            {
                for (var i = 0; i < props.Count && added < 10; i++)
                {
                    var p = props[i];
                    if (p == null) continue;
                    if (p.GetIndexParameters().Length != 0) continue;
                    var taskish = IsTaskishName(p.Name);
                    var typed = p.PropertyType != null && typeof(PlayerTask).IsAssignableFrom(p.PropertyType);
                    if (!taskish && !typed) continue;
                    msg += (added == 0 ? "" : ", ") + p.Name + ":" + (p.PropertyType != null ? p.PropertyType.Name : "null");
                    added++;
                }
            }

            MalumMenu.Log.LogInfo(msg);
        }
        catch (Exception e)
        {
            MalumMenu.Log.LogInfo($"TaskAutomation: task diagnostics failed minigameType={minigameType.Name} err={e.GetType().Name}");
        }
    }

    public void OnMinigameBegin(Minigame minigame)
    {
        if (minigame == null) return;
        if (!Utils.isInGame)
        {
            if (CheatToggles.debugTaskAutomation)
            {
                var t = minigame.GetType();
                if (t != null && LoggedIgnoredNotInGame.Add(t))
                {
                    MalumMenu.Log.LogInfo($"TaskAutomation: OnMinigameBegin ignored (not in game) mg={t.Name}");
                }
            }
            return;
        }
        if (!Utils.isPlayer)
        {
            if (CheatToggles.debugTaskAutomation)
            {
                var t = minigame.GetType();
                if (t != null && LoggedIgnoredNotInGame.Add(t))
                {
                    MalumMenu.Log.LogInfo($"TaskAutomation: OnMinigameBegin ignored (no local player) mg={t.Name}");
                }
            }
            return;
        }

        if (!TryGetTaskFromMinigame(minigame, out var task) || task == null)
        {
            if (CheatToggles.debugTaskAutomation)
            {
                var t = minigame.GetType();
                if (t != null && LoggedMissingTask.Add(t))
                {
                    TaskGetterDescByType.TryGetValue(t, out var desc);
                    MalumMenu.Log.LogInfo($"TaskAutomation: minigame={t.Name} has no task reference getter={desc ?? "null"}");
                    LogMinigameTaskDiagnostics(t);
                }
            }
            return;
        }

        var isLocalTask = false;
        try
        {
            if (Utils.isPlayer && PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.myTasks != null)
            {
                var tasks = PlayerControl.LocalPlayer.myTasks;
                for (var i = 0; i < tasks.Count; i++)
                {
                    if (ReferenceEquals(tasks[i], task))
                    {
                        isLocalTask = true;
                        break;
                    }
                }
            }
        }
        catch
        {
            isLocalTask = false;
        }
        if (!isLocalTask)
        {
            if (CheatToggles.debugTaskAutomation)
            {
                MalumMenu.Log.LogInfo($"TaskAutomation: ignoring non-local task mg={minigame.GetType().Name} taskType={task.GetType().Name}");
            }
            return;
        }
        if (task.IsComplete) return;

        var now = Time.realtimeSinceStartup;

        _minigame = minigame;
        _task = task;
        _active = true;
        _closingFromAuto = false;
        _start = now;
        _nextCompleteAttemptTime = 0f;
        _completeAttempts = 0;
        _waitingForCompleteAck = false;
        _completeAckDeadline = 0f;
        _mapId = Utils.GetCurrentMapID();
        _taskType = task is NormalPlayerTask npt ? (int)npt.TaskType : -1;
        _taskId = (int)task.Id;

        if (CheatToggles.debugTaskAutomation)
        {
            var mt = minigame.GetType();
            TaskGetterDescByType.TryGetValue(mt, out var mdesc);
            if (mt != null && LoggedGetterInfo.Add(mt))
            {
                MalumMenu.Log.LogInfo($"TaskAutomation: getter minigameType={mt.Name} getter={mdesc ?? "null"}");
            }
            MalumMenu.Log.LogInfo($"TaskAutomation: opened map={_mapId} taskId={_taskId} taskType={_taskType} mg={minigame.GetType().Name}");
        }

        if (!CheatToggles.autoTaskOnOpen)
        {
            _auto = false;
            _duration = 0f;
            _end = 0f;
            if (CheatToggles.debugTaskAutomation)
            {
                MalumMenu.Log.LogInfo($"TaskAutomation: tracking manual task map={_mapId} taskId={_taskId} taskType={_taskType} mg={minigame.GetType().Name}");
            }
            return;
        }

        var duration = MalumMenu.autoTaskDefaultSeconds.Value;

        var hasBest = TaskTimeStore.TryGetBest(_mapId, _taskId, _taskType, out var best);
        if (CheatToggles.autoTaskUseBestTime && hasBest)
        {
            duration = best;
            if (CheatToggles.debugTaskAutomation)
            {
                MalumMenu.Log.LogInfo($"TaskAutomation: using best time={best:0.00}s map={_mapId} taskId={_taskId} taskType={_taskType}");
            }
        }
        else if (CheatToggles.debugTaskAutomation && hasBest)
        {
            MalumMenu.Log.LogInfo($"TaskAutomation: best time available but disabled best={best:0.00}s map={_mapId} taskId={_taskId} taskType={_taskType}");
        }

        if (duration < 0.1f) duration = 0.1f;

        _auto = true;
        _duration = duration;
        _end = now + duration;

        if (CheatToggles.autoTaskShowProgress)
        {
            EnsureBarUi();
            SetBarVisible(true);
            UpdateBar(now);
        }
        else
        {
            SetBarVisible(false);
        }

        if (CheatToggles.debugTaskAutomation)
        {
            MalumMenu.Log.LogInfo($"TaskAutomation: scheduled map={_mapId} taskId={_taskId} taskType={_taskType} duration={duration:0.00}s mg={minigame.GetType().Name}");
        }
    }

    public void OnMinigameClosePrefix(Minigame minigame)
    {
        if (!_active) return;
        if (minigame == null) return;
        if (!ReferenceEquals(minigame, _minigame)) return;

        if (CheatToggles.debugTaskAutomation)
        {
            var now0 = Time.realtimeSinceStartup;
            var left0 = _auto ? (_end - now0) : 0f;
            MalumMenu.Log.LogInfo($"TaskAutomation: close prefix auto={_auto} closingFromAuto={_closingFromAuto} left={left0:0.00}s mg={minigame.GetType().Name}");
        }

        if (_auto && !_closingFromAuto)
        {
            var now = Time.realtimeSinceStartup;
            if (now < _end)
            {
                if (_task != null && _task.IsComplete)
                {
                    return;
                }

                if (CheatToggles.debugTaskAutomation)
                {
                    var left = _end - now;
                    MalumMenu.Log.LogInfo($"TaskAutomation: closed early, clearing state left={left:0.00}s mg={minigame.GetType().Name}");
                }
                Clear();
                return;
            }
        }
    }

    public void OnMinigameClosePostfix(Minigame minigame)
    {
        if (!_active) return;
        if (minigame == null) return;
        if (!ReferenceEquals(minigame, _minigame)) return;

        var now = Time.realtimeSinceStartup;

        if (ShouldRecordTimes() && _task != null && _task.IsComplete)
        {
            var elapsed = now - _start;
            if (elapsed > 0.01f)
            {
                TaskTimeStore.Record(_mapId, _taskId, _taskType, elapsed);
                if (CheatToggles.debugTaskAutomation)
                {
                    MalumMenu.Log.LogInfo($"TaskAutomation: recorded time={elapsed:0.00}s map={_mapId} taskId={_taskId} taskType={_taskType}");
                }
            }
        }
        else if (CheatToggles.debugTaskAutomation)
        {
            var complete = false;
            try { complete = _task != null && _task.IsComplete; } catch { complete = false; }
            MalumMenu.Log.LogInfo($"TaskAutomation: closed complete={complete} auto={_auto} mg={minigame.GetType().Name}");
        }

        SetBarVisible(false);
        Clear();
    }

    private void Update()
    {
        if (!_active)
        {
            SetBarVisible(false);
            if (!Utils.isInGame) return;
            if (!Utils.isPlayer) return;
            if (!CheatToggles.autoTaskOnOpen && !CheatToggles.recordTaskTimes) return;

            var scanNow = Time.realtimeSinceStartup;
            if (scanNow < _nextScanTime) return;
            _nextScanTime = scanNow + 0.25f;

            Minigame mg = null;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Minigame>(true);
                if (all != null)
                {
                    for (var i = 0; i < all.Length; i++)
                    {
                        var m = all[i];
                        if (m == null) continue;
                        if (!m.isActiveAndEnabled) continue;
                        mg = m;
                        break;
                    }
                }
            }
            catch
            {
                mg = null;
            }

            if (mg != null && mg.isActiveAndEnabled && !ReferenceEquals(mg, _lastSeenMinigame))
            {
                _lastSeenMinigame = mg;
                if (CheatToggles.debugTaskAutomation)
                {
                    MalumMenu.Log.LogInfo($"TaskAutomation: detected active minigame via scan mg={mg.GetType().Name}");
                }
                OnMinigameBegin(mg);
            }

            return;
        }

        if (_active && (_minigame == null || !_minigame))
        {
            if (CheatToggles.debugTaskAutomation)
            {
                MalumMenu.Log.LogInfo("TaskAutomation: minigame destroyed, clearing state");
            }
            Clear();
            return;
        }

        if (!_active) return;
        if (!_auto) return;
        if (_task == null || _minigame == null) { Clear(); return; }
        if (_task.IsComplete)
        {
            if (ShouldRecordTimes())
            {
                _auto = false;
                _end = 0f;
                SetBarVisible(false);
                if (CheatToggles.debugTaskAutomation)
                {
                    MalumMenu.Log.LogInfo($"TaskAutomation: task completed before auto end (recording mode) map={_mapId} taskId={_taskId} taskType={_taskType}");
                }
                return;
            }

            SetBarVisible(false);
            Clear();
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (CheatToggles.autoTaskShowProgress)
        {
            UpdateBar(now);
            SetBarVisible(true);
        }
        else
        {
            SetBarVisible(false);
        }

        if (_waitingForCompleteAck)
        {
            if (_task.IsComplete)
            {
                CloseMinigameFromAuto("ack complete");
                return;
            }

            if (now < _completeAckDeadline) return;
            _waitingForCompleteAck = false;
        }

        if (now < _end) return;
        if (_nextCompleteAttemptTime > 0f && now < _nextCompleteAttemptTime) return;

        _completeAttempts++;
        _nextCompleteAttemptTime = now + 0.5f;

        var localOk = TryLocalComplete(_task, out var localDetail);
        var rpcOk = Utils.TryCompleteTask(_task, out var rpcDetail);
        var isCompleteNow = false;
        try { isCompleteNow = _task.IsComplete; } catch { isCompleteNow = false; }

        if (CheatToggles.debugTaskAutomation)
        {
            MalumMenu.Log.LogInfo($"TaskAutomation: complete attempt={_completeAttempts} local={localOk} rpc={rpcOk} completeNow={isCompleteNow} localDetail={localDetail ?? "null"} rpcDetail={rpcDetail ?? "null"}");
        }

        if (isCompleteNow)
        {
            CloseMinigameFromAuto("complete immediate");
            return;
        }

        if (rpcOk)
        {
            _waitingForCompleteAck = true;
            _completeAckDeadline = now + 0.75f;
            return;
        }

        if (_completeAttempts >= 5)
        {
            if (CheatToggles.debugTaskAutomation)
            {
                MalumMenu.Log.LogInfo($"TaskAutomation: stopping auto after attempts={_completeAttempts} map={_mapId} taskId={_taskId} taskType={_taskType}");
            }
            _auto = false;
            _end = 0f;
            SetBarVisible(false);
        }
    }

    private void CloseMinigameFromAuto(string reason)
    {
        _closingFromAuto = true;
        try
        {
            SetBarVisible(false);
            if (CheatToggles.debugTaskAutomation)
            {
                MalumMenu.Log.LogInfo($"TaskAutomation: closing minigame reason={reason ?? "null"} mg={_minigame.GetType().Name}");
            }
            _minigame.Close();
        }
        catch
        {
        }
        finally
        {
            _closingFromAuto = false;
        }
    }

    private static bool TryLocalComplete(PlayerTask task, out string detail)
    {
        detail = null;
        if (task == null) { detail = "task null"; return false; }
        try
        {
            var m = AccessTools.Method(task.GetType(), "Complete");
            if (m == null) { detail = "no Complete()"; return false; }
            if (m.GetParameters().Length != 0) { detail = "Complete() has params"; return false; }
            m.Invoke(task, null);
            detail = "ok";
            return true;
        }
        catch
        {
            detail = "threw";
            return false;
        }
    }

    private void Clear()
    {
        SetBarVisible(false);
        _minigame = null;
        _task = null;
        _active = false;
        _auto = false;
        _closingFromAuto = false;
        _start = 0f;
        _end = 0f;
        _duration = 0f;
        _mapId = 0;
        _taskType = -1;
        _taskId = 0;
        _nextScanTime = 0f;
        _nextCompleteAttemptTime = 0f;
        _completeAttempts = 0;
        _waitingForCompleteAck = false;
        _completeAckDeadline = 0f;
    }
}
