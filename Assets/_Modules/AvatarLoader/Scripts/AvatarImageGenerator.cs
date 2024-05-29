using UnityEngine;

public static class AvatarImageGenerator 
{
    public static readonly int TEXTURE_HEIGHT = 200;
    public static readonly int TEXTURE_WIDTH = 200;

    public static Texture2D TakeScreenshot(Camera screenshotCamera)
    {
        screenshotCamera.Render();

        RenderTexture.active = screenshotCamera.targetTexture;

        var texture = new Texture2D(TEXTURE_WIDTH, TEXTURE_HEIGHT, TextureFormat.ARGB32, false);

        Rect rect = new Rect(0, 0, TEXTURE_WIDTH, TEXTURE_HEIGHT);

        texture.ReadPixels(rect, 0, 0);

        texture.Apply();

        return texture;
    }
}
