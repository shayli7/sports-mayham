using System.Threading;
using UnityEngine;

/// <summary>
/// On-screen performance counter for the Phase 0 tests.
/// Shows render FPS, frame time, tracking FPS (pose results per second),
/// the lowest render FPS seen, and how long the session has been running (for the 10-minute heat test).
/// Every 10 seconds it also writes a line to Logcat ("[PerfHUD]") so you can follow it with:
///   adb logcat -s Unity | findstr PerfHUD
///
/// Setup: put this on any GameObject in the scene, then call
/// PerformanceHud.ReportTrackingResult() once per pose result (see the bottom of this file).
/// </summary>
public class PerformanceHud : MonoBehaviour
{
    [Tooltip("Seconds between counter updates.")]
    [SerializeField] float updateInterval = 0.5f;

    [Tooltip("Unity caps mobile games at 30 FPS by default. Set to -1 to keep Unity's default.")]
    [SerializeField] int targetFrameRate = 60;

    [Tooltip("Tracking FPS shows green at or above this value and red below it.")]
    [SerializeField] float trackingFpsGoal = 24f;

    [Tooltip("Seconds between Logcat lines. 0 turns logging off.")]
    [SerializeField] float logInterval = 10f;

    // Written from MediaPipe's callback thread, so it is updated with Interlocked.
    static int trackingResults;

    int frames;
    float elapsed;
    float logTimer;
    float renderFps, frameMs, trackingFps;
    float minRenderFps = float.MaxValue;

    GUIStyle style;
    Texture2D background;

    /// <summary>Call once for every pose result you receive. Safe to call from any thread.</summary>
    public static void ReportTrackingResult() => Interlocked.Increment(ref trackingResults);

    void Awake()
    {
        if (targetFrameRate > 0)
        {
            QualitySettings.vSyncCount = 0; // needed for targetFrameRate to apply in the Editor or on desktop
            Application.targetFrameRate = targetFrameRate;
        }
        Interlocked.Exchange(ref trackingResults, 0);
    }

    void Update()
    {
        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < updateInterval) return;

        renderFps = frames / elapsed;
        frameMs = elapsed * 1000f / frames;
        trackingFps = Interlocked.Exchange(ref trackingResults, 0) / elapsed;

        // Skip the first few seconds: loading spikes would ruin the minimum.
        if (Time.realtimeSinceStartup > 5f)
            minRenderFps = Mathf.Min(minRenderFps, renderFps);

        if (logInterval > 0f)
        {
            logTimer += elapsed;
            if (logTimer >= logInterval)
            {
                logTimer = 0f;
                Debug.Log($"[PerfHUD] t={SessionTime()} render={renderFps:F1} min={MinText()} " +
                          $"frame={frameMs:F1}ms tracking={trackingFps:F1}");
            }
        }

        frames = 0;
        elapsed = 0f;
    }

    void OnGUI()
    {
        if (style == null)
        {
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            background.Apply();

            style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
            style.normal.background = background;
            style.padding = new RectOffset(12, 12, 8, 8);
        }

        style.fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.025f));

        string trackColor = trackingFps >= trackingFpsGoal ? "#4CFF6A" : "#FF5050";
        string text =
            $"Render: <b>{renderFps:F0}</b> FPS  ({frameMs:F1} ms)\n" +
            $"Tracking: <b><color={trackColor}>{trackingFps:F1}</color></b> FPS\n" +
            $"Min render: {MinText()}   Time: {SessionTime()}";

        // Keep it inside the safe area so the notch or rounded corners don't cover it.
        Rect safe = Screen.safeArea;
        Vector2 size = style.CalcSize(new GUIContent(text));
        float x = safe.x + 16f;
        float y = (Screen.height - safe.yMax) + 16f; // safeArea starts at the bottom, OnGUI starts at the top
        GUI.Label(new Rect(x, y, size.x, size.y), text, style);
    }

    void OnDestroy()
    {
        if (background != null) Destroy(background);
    }

    string MinText() => minRenderFps == float.MaxValue ? "-" : minRenderFps.ToString("F0");

    static string SessionTime()
    {
        int s = Mathf.FloorToInt(Time.realtimeSinceStartup);
        return $"{s / 60:00}:{s % 60:00}";
    }
}

/*
 TRACKING FPS HOOKUP
 -------------------
 Already wired into the MediaPipe sample's PoseLandmarkerRunner.cs (look for "Sports Mayhem").
 The sample is compiled in its own assembly (Mediapipe.Unity.Sample), so its .asmdef
 references this folder's SportsMayhem.asmdef. That is what lets it call PerformanceHud.

 Note: re-importing the sample from Package Manager overwrites both edits. Re-apply them,
 or move to our own runner script (planned for Phase 1).
*/
