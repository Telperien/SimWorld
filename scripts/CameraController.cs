using Godot;

public partial class CameraController : Camera2D
{
    private static readonly float[] ZoomLevels = { 1f, 2f, 4f, 8f };
    private const float PanSpeed = 400f;

    private int _zoomIndex = 0;

    public override void _Ready()
    {
        MakeCurrent();
        ApplyZoom();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoomIndex(_zoomIndex + 1);
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoomIndex(_zoomIndex - 1);
            }
        }
    }

    public override void _Process(double delta)
    {
        Vector2 direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        Position += direction * PanSpeed * (float)delta / Zoom.X;
        Position = Position.Round();
    }

    private void SetZoomIndex(int index)
    {
        _zoomIndex = Mathf.Clamp(index, 0, ZoomLevels.Length - 1);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        float level = ZoomLevels[_zoomIndex];
        Zoom = new Vector2(level, level);
    }
}
