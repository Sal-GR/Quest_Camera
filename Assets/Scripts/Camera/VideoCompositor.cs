using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Meta.XR;
using Unity.WebRTC;

/// <summary>
/// Owns all passthrough camera access, RenderTexture composition, and stereo shader blit.
/// Raises OnTrackReady once the VideoStreamTrack is live and compositing has begun.
/// </summary>
public class VideoCompositor : MonoBehaviour
{
  [Header("Passthrough Cameras")]
  [SerializeField] private PassthroughCameraAccess passthroughLeft;
  [SerializeField] private PassthroughCameraAccess passthroughRight;

  [Header("Compositor Cameras")]
  [SerializeField] private Camera LeftCaptureCamera;
  [SerializeField] private Camera RightCaptureCamera;

  [Header("Render Textures")]
  [SerializeField] private RenderTexture LeftCameraRT;
  [SerializeField] private RenderTexture RightCameraRT;

  // Fired once the VideoStreamTrack is created and compositing has started.
  public event Action<VideoStreamTrack> OnTrackReady;

  private RenderTexture _webRtcRenderTexture;
  private Material _stereoBlendMaterial;
  private VideoStreamTrack _videoTrack;
  private bool _isReady = false;

  public bool IsReady => _isReady;
  public VideoStreamTrack Track => _videoTrack;

  // Called by WebRTCSender after the peer connection is set up.
  public void StartCompositor()
  {
      StartCoroutine(SetupVideoTrack());
  }

  private IEnumerator SetupVideoTrack()
  {
      // Wait until both passthrough cameras are streaming
      float timeout = 10f;
      float elapsed = 0f;
      while ((!passthroughLeft.IsPlaying || !passthroughRight.IsPlaying) && elapsed < timeout)
      {
          yield return new WaitForSeconds(0.2f);
          elapsed += 0.2f;
      }

      if (!passthroughLeft.IsPlaying || !passthroughRight.IsPlaying)
      {
          Debug.LogError("VideoCompositor: PassthroughCameraAccess never started playing!");
          yield break;
      }

      // Let two frames settle so GetTexture() returns a valid RenderTexture
      yield return new WaitForEndOfFrame();
      yield return new WaitForEndOfFrame();

      var sourceRt = passthroughLeft.GetTexture() as RenderTexture;
      if (sourceRt == null)
      {
          Debug.LogError("VideoCompositor: Could not get RenderTexture from PassthroughCameraAccess!");
          yield break;
      }

      // Create the output RenderTexture that WebRTC will encode
      _webRtcRenderTexture = new RenderTexture(sourceRt.width, sourceRt.height, 0)
      {
          useMipMap = false,
          autoGenerateMips = false,
          graphicsFormat = GraphicsFormat.B8G8R8A8_SRGB
      };
      _webRtcRenderTexture.Create();

      AlignCaptureCamerasToLens();

      // Load the stereo blend shader
      var shader = Shader.Find("Custom/StereoBlend");
      if (shader == null)
      {
          Debug.LogError("VideoCompositor: Could not find shader Custom/StereoBlend!");
          yield break;
      }
      _stereoBlendMaterial = new Material(shader);

      // Create the track and notify listeners (e.g. PeerConnectionManager)
      _videoTrack = new VideoStreamTrack(_webRtcRenderTexture);
      _isReady = true;

      OnTrackReady?.Invoke(_videoTrack);

      StartCoroutine(CompositorLoop());
  }

  private IEnumerator CompositorLoop()
  {
      while (true)
      {
          yield return new WaitForEndOfFrame();

          if (!_isReady || _webRtcRenderTexture == null) continue;
          if (!passthroughLeft.IsPlaying || !passthroughRight.IsPlaying) continue;

          var leftSrc  = passthroughLeft.GetTexture();
          var rightSrc = passthroughRight.GetTexture();
          if (leftSrc == null || rightSrc == null) continue;

          _stereoBlendMaterial.SetTexture("_LeftTex",     leftSrc);
          _stereoBlendMaterial.SetTexture("_RightTex",    rightSrc);
          _stereoBlendMaterial.SetTexture("_LeftAssets",  LeftCameraRT);
          _stereoBlendMaterial.SetTexture("_RightAssets", RightCameraRT);

          Graphics.Blit(null, _webRtcRenderTexture, _stereoBlendMaterial);
      }
  }

  private void AlignCaptureCamerasToLens()
  {
      var leftLens  = passthroughLeft.Intrinsics.LensOffset;
      var rightLens = passthroughRight.Intrinsics.LensOffset;

      LeftCaptureCamera.transform.localPosition  = leftLens.position;
      LeftCaptureCamera.transform.localRotation  = leftLens.rotation;
      RightCaptureCamera.transform.localPosition = rightLens.position;
      RightCaptureCamera.transform.localRotation = rightLens.rotation;

      float fovLeft  = 2f * Mathf.Atan(passthroughLeft.CurrentResolution.y  / (2f * passthroughLeft.Intrinsics.FocalLength.y))  * Mathf.Rad2Deg;
      float fovRight = 2f * Mathf.Atan(passthroughRight.CurrentResolution.y / (2f * passthroughRight.Intrinsics.FocalLength.y)) * Mathf.Rad2Deg;

      LeftCaptureCamera.fieldOfView  = fovLeft;
      RightCaptureCamera.fieldOfView = fovRight;
  }

  private void OnDestroy()
  {
      _isReady = false;
      _videoTrack?.Dispose();
      if (_webRtcRenderTexture != null) Destroy(_webRtcRenderTexture);
      if (LeftCameraRT  != null) Destroy(LeftCameraRT);
      if (RightCameraRT != null) Destroy(RightCameraRT);
  }
}