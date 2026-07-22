using UnityEngine;

public static class AvatarImageGenerator 
{
    public static readonly int TEXTURE_HEIGHT = 200;
    public static readonly int TEXTURE_WIDTH = 200;

    public static Texture2D TakeScreenshot(Camera screenshotCamera)
    {
        if (screenshotCamera == null || screenshotCamera.targetTexture == null)
        {
            Debug.LogError("Avatar screenshot camera or target texture is missing.");
            return null;
        }

        RenderTexture previousRenderTexture = RenderTexture.active;
        try
        {
            screenshotCamera.Render();
            RenderTexture.active = screenshotCamera.targetTexture;

            var texture = new Texture2D(TEXTURE_WIDTH, TEXTURE_HEIGHT, TextureFormat.ARGB32, false)
            {
                name = "AvatarThumbnail",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.ReadPixels(new Rect(0, 0, TEXTURE_WIDTH, TEXTURE_HEIGHT), 0, 0);
            texture.Apply(false, false);
            return texture;
        }
        finally
        {
            RenderTexture.active = previousRenderTexture;
        }
    }
}
