shader_type canvas_item;

uniform vec4 outline_color : source_color = vec4(1.0, 0.55, 0.0, 1.0); // naranja
uniform float outline_size : hint_range(0.5, 4.0) = 1.5;
uniform bool show_outline = false;

void fragment() {
    vec4 col = texture(TEXTURE, UV);

    if (show_outline) {
        float a = col.a;
        a = max(a, texture(TEXTURE, UV + vec2( outline_size * TEXTURE_PIXEL_SIZE.x, 0.0)).a);
        a = max(a, texture(TEXTURE, UV + vec2(-outline_size * TEXTURE_PIXEL_SIZE.x, 0.0)).a);
        a = max(a, texture(TEXTURE, UV + vec2(0.0,  outline_size * TEXTURE_PIXEL_SIZE.y)).a);
        a = max(a, texture(TEXTURE, UV + vec2(0.0, -outline_size * TEXTURE_PIXEL_SIZE.y)).a);

        // Pinta el outline solo en los píxeles que son borde (transparentes rodeados de opaco)
        if (col.a < 0.5 && a > 0.5)
            col = outline_color;
    }

    COLOR = col;
}
