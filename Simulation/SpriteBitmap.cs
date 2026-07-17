namespace Simulation;

// Buffer de pixels neutre (aucun type Godot en signature) : /Simulation
// reste pure C#, seul /Game convertit ça en Image/ImageTexture Godot.
// RGBA, 4 octets par pixel, ligne par ligne.
public sealed class SpriteBitmap
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public SpriteBitmap(int width, int height)
    {
        Width = width;
        Height = height;
        Rgba = new byte[width * height * 4];
    }

    public void SetPixel(int x, int y, uint colorRgb, byte alpha = 255)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        int offset = (y * Width + x) * 4;
        Rgba[offset] = (byte)((colorRgb >> 16) & 0xFF);
        Rgba[offset + 1] = (byte)((colorRgb >> 8) & 0xFF);
        Rgba[offset + 2] = (byte)(colorRgb & 0xFF);
        Rgba[offset + 3] = alpha;
    }

    public bool IsTransparentAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return true;
        }

        return Rgba[(y * Width + x) * 4 + 3] == 0;
    }

    public bool Equals(SpriteBitmap other)
    {
        if (Width != other.Width || Height != other.Height)
        {
            return false;
        }

        for (int i = 0; i < Rgba.Length; i++)
        {
            if (Rgba[i] != other.Rgba[i])
            {
                return false;
            }
        }

        return true;
    }

    // Miroir horizontal exact -- utilisé pour dériver Facing=1 depuis le
    // buffer canonique Facing=0 plutôt que de régénérer indépendamment
    // (garantit la relation miroir par construction, pas par coïncidence
    // de deux tirages RNG séparés).
    public SpriteBitmap MirroredHorizontally()
    {
        var mirrored = new SpriteBitmap(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int srcOffset = (y * Width + (Width - 1 - x)) * 4;
                int dstOffset = (y * Width + x) * 4;
                mirrored.Rgba[dstOffset] = Rgba[srcOffset];
                mirrored.Rgba[dstOffset + 1] = Rgba[srcOffset + 1];
                mirrored.Rgba[dstOffset + 2] = Rgba[srcOffset + 2];
                mirrored.Rgba[dstOffset + 3] = Rgba[srcOffset + 3];
            }
        }

        return mirrored;
    }
}
