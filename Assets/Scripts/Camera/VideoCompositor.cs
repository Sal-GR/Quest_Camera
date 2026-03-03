using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Meta.XR;
using Unity.WebRTC;

public class VideoCompositor : MonoBehaviour
{
    [Header ("Dependencies")]
    [SerializeField] private PassthroughManager passthroughManager;

  [Header("Render Textures")]
  [SerializeField] private RenderTexture LeftCameraRT;
  //[SerializeField] private RenderTexture RightCameraRT;

  // Fired once the VideoStreamTrack is created and compositing has started.
  public event Action<VideoStreamTrack> OnTrackReady;

  private RenderTexture _webRtcRenderTexture;
  private Material _stereoBlendMaterial;
  private VideoStreamTrack _videoTrack;
  public bool IsReady { get; private set; } = false;

//   public bool IsReady => _isReady;
  public VideoStreamTrack Track => _videoTrack;

  // Called by WebRTCSender after the peer connection is set up.
  public void StartCompositor()
  {
    if (passthroughManager.IsReady)
    {
        StartCoroutine(SetupRenderPipeline());
    }
    else
    {
        passthroughManager.OnPassthroughReady += OnPassthroughReady;
        passthroughManager.StartPassthrough();
    }
  }
      private void OnPassthroughReady()
    {
        passthroughManager.OnPassthroughReady -= OnPassthroughReady;
        StartCoroutine(SetupRenderPipeline());
    }

    private IEnumerator SetupRenderPipeline()
    {
        var sourceRt = passthroughManager.GetLeftRenderTexture();
        if (sourceRt == null)
        {
            Debug.LogError("VideoCompositor: Could not get RenderTexture from PassthroughManager!");
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

        var shader = Shader.Find("Custom/StereoBlend");
        if (shader == null)
        {
            Debug.LogError("VideoCompositor: Could not find shader Custom/StereoBlend!");
            yield break;
        }
        _stereoBlendMaterial = new Material(shader);

        _videoTrack = new VideoStreamTrack(_webRtcRenderTexture);
        IsReady = true;

        OnTrackReady?.Invoke(_videoTrack);
        Debug.Log("VideoCompositor: Track ready, starting compositor loop");

        StartCoroutine(CompositorLoop());
    }

  private IEnumerator CompositorLoop()
  {
      while (true)
      {
          yield return new WaitForEndOfFrame();

          if (!IsReady || _webRtcRenderTexture == null) continue;
          if (!passthroughManager.IsPlaying) continue;

          var leftSrc  = passthroughManager.GetLeftTexture();
          //var rightSrc = passthroughManager.GetRightTexture();
          if (leftSrc == null) continue;

          _stereoBlendMaterial.SetTexture("_LeftTex",     leftSrc);
          //_stereoBlendMaterial.SetTexture("_RightTex",    rightSrc);
          _stereoBlendMaterial.SetTexture("_LeftAssets",  LeftCameraRT);
          //_stereoBlendMaterial.SetTexture("_RightAssets", RightCameraRT);

          Graphics.Blit(null, _webRtcRenderTexture, _stereoBlendMaterial);
      }
  }

  private void OnDestroy()
  {
      IsReady = false;
      _videoTrack?.Dispose();
      if (_webRtcRenderTexture != null) Destroy(_webRtcRenderTexture);
      if (LeftCameraRT  != null) Destroy(LeftCameraRT);
      //if (RightCameraRT != null) Destroy(RightCameraRT);
  }
}