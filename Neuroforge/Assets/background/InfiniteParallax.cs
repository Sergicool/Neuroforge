using Godot;
using System;

public partial class InfiniteParallax : Node2D
{
    [ExportGroup("Movement config")]
    [Export] public Vector2 Direction = Vector2.Left;
    [Export] public float BaseSpeed = 50.0f;
    [Export] public float LayerSpeedMultipler = 0.5f;

    [ExportGroup("Background textures")]
    [Export] public Godot.Collections.Array<Texture2D> Layers = new();
    [Export] public Godot.Collections.Array<Color> LayerModulates = new();

    public override void _Ready()
    {
        // Asegurar que el Viewport haya calculado bien su tamaño antes de configurar el parallax
        CallDeferred(MethodName.SetupParallax);
    }

    private void SetupParallax()
    {
        foreach (Node child in GetChildren()) child.QueueFree();

        // Obtenemos el tamaño real de la ventana/pantalla del juego
        Vector2 screenSize = GetViewportRect().Size;

        for (int i = 0; i < Layers.Count; i++)
        {
            if (Layers[i] == null) continue;

            Parallax2D parallaxLayer = new Parallax2D();
            Sprite2D sprite = new Sprite2D();

            sprite.Texture = Layers[i];
            sprite.Centered = false;

            Vector2 texSize = Layers[i].GetSize();

            // Calcular escala necesaria para que la imagen cubra al menos el alto de la pantalla
            float scaleRatio = screenSize.Y / texSize.Y;
            sprite.Scale = new Vector2(scaleRatio, scaleRatio);

            // El RepeatSize = tamaño de la textura * escala
            parallaxLayer.RepeatSize = new Vector2(texSize.X * scaleRatio, 0);

            parallaxLayer.RepeatTimes = 3;

            // Las capas lejanas mas lentas
            float depthFactor = (float)(i + 1) / Layers.Count;
            parallaxLayer.Autoscroll = Direction * (BaseSpeed * depthFactor * LayerSpeedMultipler);

            // Transparencia
            if (i < LayerModulates.Count)
            {
                sprite.Modulate = LayerModulates[i];
            }

            parallaxLayer.AddChild(sprite);
            AddChild(parallaxLayer);
        }
    }
}