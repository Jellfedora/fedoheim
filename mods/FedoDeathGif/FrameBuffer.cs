using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FedoDeathGif
{
    public class FrameSnapshot
    {
        public int Width;
        public int Height;
        public List<byte[]> Frames;
    }

    // Capture l'écran en continu à basse résolution et ne garde que les N dernières secondes.
    public class FrameBuffer : MonoBehaviour
    {
        private int _width;
        private int _height;
        private int _fps;
        private int _maxFrames;

        private readonly Queue<byte[]> _frames = new Queue<byte[]>();
        private RenderTexture _downscaleRT;
        private RenderTexture _fullResRT;
        private Texture2D _readTex;
        private int _fullResWidth;
        private int _fullResHeight;

        public void Configure(int width, int height, int fps, float bufferSeconds)
        {
            _width = width;
            _height = height;
            _fps = Mathf.Max(1, fps);
            _maxFrames = Mathf.Max(1, Mathf.RoundToInt(bufferSeconds * _fps));

            _downscaleRT = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32);
            _readTex = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            EnsureFullResTarget();

            StartCoroutine(CaptureLoop());
        }

        // ScreenCapture.CaptureScreenshotIntoRenderTexture copies pixels 1:1 -- it does not
        // scale to fit the target. It needs a render texture matching the screen's actual
        // resolution, which we then downscale ourselves with a GPU-side Blit (cheap, and it
        // handles the orientation correctly, unlike a raw 1:1 copy into a smaller texture).
        private void EnsureFullResTarget()
        {
            int screenWidth = Mathf.Max(1, Screen.width);
            int screenHeight = Mathf.Max(1, Screen.height);
            if (_fullResRT != null && _fullResWidth == screenWidth && _fullResHeight == screenHeight)
            {
                return;
            }

            if (_fullResRT != null)
            {
                _fullResRT.Release();
                Destroy(_fullResRT);
            }

            _fullResRT = new RenderTexture(screenWidth, screenHeight, 0, RenderTextureFormat.ARGB32);
            _fullResWidth = screenWidth;
            _fullResHeight = screenHeight;
        }

        private IEnumerator CaptureLoop()
        {
            var wait = new WaitForSeconds(1f / _fps);
            while (true)
            {
                yield return wait;
                if (Player.m_localPlayer != null)
                {
                    CaptureFrame();
                }
            }
        }

        private void CaptureFrame()
        {
            EnsureFullResTarget();

            // Capture plein écran dans une RenderTexture persistante (tout se passe sur le GPU,
            // rien ne remonte au CPU ici), puis on la redimensionne avec un Blit GPU-à-GPU bon
            // marché. On évite ainsi le coût énorme d'un readback CPU en pleine résolution.
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_fullResRT);
            // CaptureScreenshotIntoRenderTexture fills the render texture bottom-to-top, so we
            // flip it vertically during the downscale blit (Blit itself doesn't correct this).
            Graphics.Blit(_fullResRT, _downscaleRT, new Vector2(1f, -1f), new Vector2(0f, 1f));

            var previousActive = RenderTexture.active;
            RenderTexture.active = _downscaleRT;
            _readTex.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
            _readTex.Apply(false);
            RenderTexture.active = previousActive;

            _frames.Enqueue(_readTex.GetRawTextureData());
            while (_frames.Count > _maxFrames)
            {
                _frames.Dequeue();
            }
        }

        // Appelé depuis le thread principal (patch Harmony) uniquement, comme CaptureFrame.
        public FrameSnapshot Snapshot()
        {
            return new FrameSnapshot
            {
                Width = _width,
                Height = _height,
                Frames = new List<byte[]>(_frames)
            };
        }
    }
}
