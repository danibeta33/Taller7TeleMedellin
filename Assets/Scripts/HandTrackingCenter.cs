using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using UnityEngine;

public class HandTrackingCenter : MonoBehaviour
{
    private const int Wrist = 0;
    private const int ThumbMcp = 2;
    private const int ThumbIp = 3;
    private const int ThumbTip = 4;
    private const int IndexMcp = 5;
    private const int IndexPip = 6;
    private const int IndexTip = 8;
    private const int MiddlePip = 10;
    private const int MiddleTip = 12;
    private const int RingPip = 14;
    private const int RingTip = 16;
    private const int PinkyMcp = 17;
    private const int PinkyPip = 18;
    private const int PinkyTip = 20;

    [Header("MediaPipe Source")]
    [SerializeField] private HandLandmarkerRunner handLandmarkerRunner;

    [Header("Gesture Thresholds")]
    [SerializeField] private float minimumHandScale = 0.05f;
    [SerializeField] private float thumbExtensionFactor = 0.18f;
    [SerializeField] private float foldedFingerFactor = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs;
    [SerializeField] private float debugLogIntervalSeconds = 0.5f;

    public readonly struct HandData
    {
        public readonly int id;
        public readonly bool isLeft;
        public readonly bool isRight;
        public readonly float handednessScore;
        public readonly IReadOnlyList<NormalizedLandmark> landmarks;
        public readonly bool isThumbsUp;

        public HandData(
            int id,
            bool isLeft,
            bool isRight,
            float handednessScore,
            IReadOnlyList<NormalizedLandmark> landmarks,
            bool isThumbsUp)
        {
            this.id = id;
            this.isLeft = isLeft;
            this.isRight = isRight;
            this.handednessScore = handednessScore;
            this.landmarks = landmarks;
            this.isThumbsUp = isThumbsUp;
        }
    }

    private readonly List<HandData> hands = new List<HandData>(4);
    private int lastProcessedFrame = -1;
    private float nextDebugLogTime;

    public IReadOnlyList<HandData> GetHands()
    {
        RefreshCacheIfNeeded();
        return hands;
    }

    public int GetDetectedHandsCount()
    {
        RefreshCacheIfNeeded();
        return hands.Count;
    }

    public int GetThumbsUpHandsCount()
    {
        RefreshCacheIfNeeded();

        var count = 0;
        for (var i = 0; i < hands.Count; i++)
        {
            if (hands[i].isThumbsUp)
            {
                count++;
            }
        }

        return count;
    }

    public bool TryGetHandLandmarkerResult(out HandLandmarkerResult result)
    {
        EnsureRunnerReference();
        if (handLandmarkerRunner != null && handLandmarkerRunner.TryGetLatestResult(out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    private void LateUpdate()
    {
        RefreshCacheIfNeeded();
    }

    private void RefreshCacheIfNeeded()
    {
        var frame = Time.frameCount;
        if (lastProcessedFrame == frame)
        {
            return;
        }

        lastProcessedFrame = frame;
        hands.Clear();

        if (!TryGetHandLandmarkerResult(out var result) || result.handLandmarks == null)
        {
            EmitDebugLog("[HandTrackingCenter] Manos detectadas: 0");
            return;
        }

        var total = result.handLandmarks.Count;
        for (var i = 0; i < total; i++)
        {
            var landmarks = result.handLandmarks[i].landmarks;
            if (landmarks == null || landmarks.Count <= PinkyTip)
            {
                continue;
            }

            var handednessInfo = ExtractHandedness(result, i);
            var thumbUp = IsThumbsUp(landmarks, out var indexClosed, out var middleClosed, out var ringClosed, out var pinkyClosed);

            hands.Add(new HandData(
                i,
                handednessInfo.isLeft,
                handednessInfo.isRight,
                handednessInfo.score,
                landmarks,
                thumbUp));

            EmitDebugLog(
                "[HandTrackingCenter] HandId:" + i +
                " Left:" + handednessInfo.isLeft +
                " Right:" + handednessInfo.isRight +
                " ThumbUp:" + thumbUp +
                " IndexClosed:" + indexClosed +
                " MiddleClosed:" + middleClosed +
                " RingClosed:" + ringClosed +
                " PinkyClosed:" + pinkyClosed);
        }

        EmitDebugLog("[HandTrackingCenter] Manos detectadas: " + hands.Count + " | ThumbsUp: " + GetThumbsUpCountFromCache());
    }

    private int GetThumbsUpCountFromCache()
    {
        var count = 0;
        for (var i = 0; i < hands.Count; i++)
        {
            if (hands[i].isThumbsUp)
            {
                count++;
            }
        }

        return count;
    }

    private (bool isLeft, bool isRight, float score) ExtractHandedness(HandLandmarkerResult result, int index)
    {
        if (result.handedness == null || index < 0 || index >= result.handedness.Count)
        {
            return (false, false, 0f);
        }

        var categories = result.handedness[index].categories;
        if (categories == null || categories.Count == 0)
        {
            return (false, false, 0f);
        }

        var category = categories[0];
        var name = (category.categoryName ?? string.Empty).Trim();
        var isLeft = name.Equals("left", StringComparison.OrdinalIgnoreCase);
        var isRight = name.Equals("right", StringComparison.OrdinalIgnoreCase);

        return (isLeft, isRight, category.score);
    }

    private bool IsThumbsUp(
        IReadOnlyList<NormalizedLandmark> landmarks,
        out bool indexClosed,
        out bool middleClosed,
        out bool ringClosed,
        out bool pinkyClosed)
    {
        var wrist = ToVector3(landmarks[Wrist]);
        var indexMcp = ToVector3(landmarks[IndexMcp]);
        var pinkyMcp = ToVector3(landmarks[PinkyMcp]);

        var handScale = Mathf.Max(minimumHandScale, Vector3.Distance(indexMcp, pinkyMcp));
        var thumbMargin = handScale * Mathf.Max(0.01f, thumbExtensionFactor);
        var foldedMargin = handScale * Mathf.Max(0.005f, foldedFingerFactor * 0.5f);

        var thumbUp = IsThumbExtendedByWristDistance(landmarks, wrist, thumbMargin);
        indexClosed = IsFingerClosedByWristDistance(landmarks, wrist, IndexPip, IndexTip, foldedMargin);
        middleClosed = IsFingerClosedByWristDistance(landmarks, wrist, MiddlePip, MiddleTip, foldedMargin);
        ringClosed = IsFingerClosedByWristDistance(landmarks, wrist, RingPip, RingTip, foldedMargin);
        pinkyClosed = IsFingerClosedByWristDistance(landmarks, wrist, PinkyPip, PinkyTip, foldedMargin);

        return thumbUp && indexClosed && middleClosed && ringClosed && pinkyClosed;
    }

    private bool IsThumbExtendedByWristDistance(IReadOnlyList<NormalizedLandmark> landmarks, Vector3 wrist, float margin)
    {
        var thumbMcp = ToVector3(landmarks[ThumbMcp]);
        var thumbIp = ToVector3(landmarks[ThumbIp]);
        var thumbTip = ToVector3(landmarks[ThumbTip]);

        var mcpToWrist = Vector3.Distance(thumbMcp, wrist);
        var ipToWrist = Vector3.Distance(thumbIp, wrist);
        var tipToWrist = Vector3.Distance(thumbTip, wrist);

        return tipToWrist > ipToWrist + margin && tipToWrist > mcpToWrist + margin;
    }

    private static bool IsFingerClosedByWristDistance(
        IReadOnlyList<NormalizedLandmark> landmarks,
        Vector3 wrist,
        int pipIndex,
        int tipIndex,
        float margin)
    {
        var pip = ToVector3(landmarks[pipIndex]);
        var tip = ToVector3(landmarks[tipIndex]);

        var tipToWrist = Vector3.Distance(tip, wrist);
        var pipToWrist = Vector3.Distance(pip, wrist);
        return tipToWrist + margin < pipToWrist;
    }

    private static Vector3 ToVector3(NormalizedLandmark landmark)
    {
        return new Vector3(landmark.x, landmark.y, landmark.z);
    }

    private void EnsureRunnerReference()
    {
        if (handLandmarkerRunner != null)
        {
            return;
        }

        var solution = GameObject.Find("Solution");
        if (solution != null)
        {
            handLandmarkerRunner = solution.GetComponent<HandLandmarkerRunner>();
        }
    }

    private void EmitDebugLog(string message)
    {
        if (!enableDebugLogs)
        {
            return;
        }

        if (Time.unscaledTime < nextDebugLogTime)
        {
            return;
        }

        nextDebugLogTime = Time.unscaledTime + Mathf.Max(0.05f, debugLogIntervalSeconds);
        Debug.Log(message);
    }
}
