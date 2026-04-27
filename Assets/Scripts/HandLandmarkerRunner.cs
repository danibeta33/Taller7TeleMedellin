// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
  public class HandLandmarkerRunner : VisionTaskApiRunner<HandLandmarker>
  {
    [SerializeField] private HandLandmarkerResultAnnotationController _handLandmarkerResultAnnotationController;
    [SerializeField] private float _imageSourceRecoveryIntervalSeconds = 0.5f;

    private Experimental.TextureFramePool _textureFramePool;
    private readonly object _latestResultLock = new object();
    private HandLandmarkerResult _latestResult;
    private bool _hasLatestResult;

    public readonly HandLandmarkDetectionConfig config = new HandLandmarkDetectionConfig();

    public bool TryGetLatestResult(out HandLandmarkerResult result)
    {
      lock (_latestResultLock)
      {
        if (!_hasLatestResult || _latestResult.handLandmarks == null)
        {
          result = default;
          return false;
        }

        result = default;
        _latestResult.CloneTo(ref result);
        return true;
      }
    }

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
      lock (_latestResultLock)
      {
        _latestResult = default;
        _hasLatestResult = false;
      }
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Image Read Mode = {config.ImageReadMode}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"NumHands = {config.NumHands}");
      Debug.Log($"MinHandDetectionConfidence = {config.MinHandDetectionConfidence}");
      Debug.Log($"MinHandPresenceConfidence = {config.MinHandPresenceConfidence}");
      Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetHandLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnHandLandmarkDetectionOutput : null);
      taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      System.Exception startImageSourceException = null;
      yield return RunSafe(imageSource.Play(), e => startImageSourceException = e);

      if (startImageSourceException != null)
      {
        Debug.LogError($"Failed to start ImageSource: {startImageSourceException.Message}");
        yield break;
      }

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      SetupAnnotationController(_handLandmarkerResultAnnotationController, imageSource);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = HandLandmarkerResult.Alloc(options.numHands);
      var nextImageSourceRecoveryTime = 0f;

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!imageSource.isPlaying)
        {
          if (Time.unscaledTime >= nextImageSourceRecoveryTime)
          {
            nextImageSourceRecoveryTime = Time.unscaledTime + Mathf.Max(0.1f, _imageSourceRecoveryIntervalSeconds);

            System.Exception recoveryException = null;
            if (imageSource.isPrepared)
            {
              yield return RunSafe(imageSource.Resume(), e => recoveryException = e);
            }
            else
            {
              yield return RunSafe(imageSource.Play(), e => recoveryException = e);
            }

            if (recoveryException != null)
            {
              Debug.LogWarning($"ImageSource recovery failed: {recoveryException.Message}");
            }
          }

          yield return null;
          continue;
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return new WaitForEndOfFrame();
          continue;
        }

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              continue;
            }
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              UpdateLatestResult(result);
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              UpdateLatestResult(default);
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              UpdateLatestResult(result);
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              UpdateLatestResult(default);
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }

    private void OnHandLandmarkDetectionOutput(HandLandmarkerResult result, Image image, long timestamp)
    {
      UpdateLatestResult(result);
      _handLandmarkerResultAnnotationController.DrawLater(result);
    }

    private void UpdateLatestResult(HandLandmarkerResult result)
    {
      lock (_latestResultLock)
      {
        result.CloneTo(ref _latestResult);
        _hasLatestResult = _latestResult.handLandmarks != null;
      }
    }

    private static IEnumerator RunSafe(IEnumerator operation, System.Action<System.Exception> onException)
    {
      while (true)
      {
        object current;
        try
        {
          if (!operation.MoveNext())
          {
            yield break;
          }

          current = operation.Current;
        }
        catch (System.Exception e)
        {
          onException?.Invoke(e);
          yield break;
        }

        yield return current;
      }
    }
  }
}
