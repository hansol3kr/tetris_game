shader_type canvas_item;

// Optional per-cell neon shader. The default renderer fakes glow with layered
// rects + the WorldEnvironment bloom, but you can assign this to a TextureRect /
// ColorRect per block for a richer rounded-capsule glow with an animated sheen.
//
// Uniforms:
//   glow_color  - the piece's neon hue
//   corner      - rounded-corner radius (0..0.5 of the cell)
//   edge_soft   - softness of the outer glow falloff
//   sheen       - moving highlight intensity

uniform vec4 glow_color : source_color = vec4(0.2, 0.9, 1.0, 1.0);
uniform float corner : hint_range(0.0, 0.5) = 0.18;
uniform float edge_soft : hint_range(0.001, 0.4) = 0.12;
uniform float sheen : hint_range(0.0, 1.0) = 0.35;

// Signed distance to a rounded box centered in UV space.
float rounded_box(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + vec2(r);
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
    vec2 p = UV - vec2(0.5);
    float d = rounded_box(p, vec2(0.5), corner);

    // Solid body inside the shape, soft glow just outside.
    float body = smoothstep(0.0, -edge_soft, d);
    float glow = smoothstep(edge_soft, 0.0, d) * 0.6;

    // Diagonal moving sheen for a glossy neon feel.
    float s = sin((UV.x + UV.y) * 6.2831 - TIME * 1.5) * 0.5 + 0.5;
    float highlight = smoothstep(0.7, 1.0, s) * sheen * body;

    vec3 col = glow_color.rgb;
    float alpha = clamp(body + glow, 0.0, 1.0);
    col += vec3(highlight);

    COLOR = vec4(col, alpha) * glow_color.a;
}
