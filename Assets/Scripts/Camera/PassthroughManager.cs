using System;
using System.Collections;
using Meta.XR;
using UnityEngine;
public class PassthroughManager : MonoBehaviour
{
  [Header("Passthrough Cameras")]
  [SerializeField] private PassthroughCameraAccess passthroughLeft;
  //[SerializeField] private PassthroughCameraAccess passthroughRight;

  [Header("Compositor Cameras")]
  [SerializeField] private Camera LeftCaptureCamera;
  //[SerializeField] private Camera RightCaptureCamera;

  // Fired once both cameras are streaming and lens alignment is done
  public event Action OnPassthroughReady;

  public bool IsReady { get; private set; } = false;

  public bool IsPlaying => passthroughLeft.IsPlaying;

  public Texture GetLeftTexture()  => passthroughLeft.GetTexture();
  //public Texture GetRightTexture() => passthroughRight.GetTexture();

  // Returns the left passthrough RenderTexture — used by VideoCompositor to
  // match the output RT dimensions.
  public RenderTexture GetLeftRenderTexture() => passthroughLeft.GetTexture() as RenderTexture;

  public void StartPassthrough()
  {
      StartCoroutine(WaitForCameras());
  }

  private IEnumerator WaitForCameras()
  {
      float timeout = 10f;
      float elapsed = 0f;
      while (!passthroughLeft.IsPlaying  && elapsed < timeout)
      {
          yield return new WaitForSeconds(0.2f);
          elapsed += 0.2f;
      }

      if (!passthroughLeft.IsPlaying)
      {
          Debug.LogError("PassthroughManager: Left camera never started playing!");
          yield break;
      }

      // Let two frames settle so GetTexture() returns a valid RenderTexture
      yield return new WaitForEndOfFrame();
      yield return new WaitForEndOfFrame();

      AlignCaptureCamerasToLens();

      IsReady = true;
      OnPassthroughReady?.Invoke();
      Debug.Log("PassthroughManager: Ready");
  }

  private void AlignCaptureCamerasToLens()
  {
      var leftLens  = passthroughLeft.Intrinsics.LensOffset;
      //var rightLens = passthroughRight.Intrinsics.LensOffset;

      LeftCaptureCamera.transform.localPosition  = leftLens.position;
      LeftCaptureCamera.transform.localRotation  = leftLens.rotation;

      float fovLeft  = 2f * Mathf.Atan(passthroughLeft.CurrentResolution.y  / (2f * passthroughLeft.Intrinsics.FocalLength.y))  * Mathf.Rad2Deg;
      //float fovRight = 2f * Mathf.Atan(passthroughRight.CurrentResolution.y / (2f * passthroughRight.Intrinsics.FocalLength.y)) * Mathf.Rad2Deg;

      LeftCaptureCamera.fieldOfView  = fovLeft;
      //RightCaptureCamera.fieldOfView = fovRight;
  }
}