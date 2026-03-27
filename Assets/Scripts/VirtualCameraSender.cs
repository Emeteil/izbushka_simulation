using System.Collections;
using UnityEngine;

public class VirtualCameraSender : MonoBehaviour
{
    [Header("Camera To Stream")]
    [SerializeField] private Camera targetCamera;

    [Header("Stream Settings")]
    [SerializeField] private int width = 640;
    [SerializeField] private int height = 480;
    [SerializeField] private float fps = 24f;
    [SerializeField] [Range(1, 100)] private int jpegQuality = 50;

    private RenderTexture _renderTexture;
    private Texture2D _texture;

    private void Start()
    {
        if (targetCamera == null) 
            targetCamera = Camera.main;

        if (targetCamera == null)
        {
            Debug.LogError("[VirtualCameraSender] No camera found!");
            enabled = false;
            return;
        }

        _renderTexture = new RenderTexture(width, height, 24);
        _texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        targetCamera.targetTexture = _renderTexture;

        StartCoroutine(SendFramesLoop());
    }

    private void OnDestroy()
    {
        if (targetCamera != null) 
            targetCamera.targetTexture = null;
    }

    private IEnumerator SendFramesLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(1f / fps);

        while (true)
        {
            yield return new WaitForEndOfFrame();

            if (TransportReceiver.Instance == null || !TransportReceiver.Instance.IsConnected)
            {
                yield return wait;
                continue;
            }

            RenderTexture.active = _renderTexture;
            _texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            _texture.Apply();
            RenderTexture.active = null;

            byte[] jpgBytes = _texture.EncodeToJPG(jpegQuality);
            TransportReceiver.Instance.SendFrame(jpgBytes);

            yield return wait;
        }
    }
}