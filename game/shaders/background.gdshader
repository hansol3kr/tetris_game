shader_type canvas_item;

// Animated "NEON FLUX" backdrop: vertical navy-violet gradient, 2-3 slowly
// drifting aurora blobs, a faint dot grid, drifting specks, vignette, and a
// 1-bit dither to kill OLED banding. One pass, no texture reads.
//
// Script-driven uniforms:
//   dim    - 0 on menus, 1 during gameplay (blobs fade, specks off)
//   motion - 1 normal, 0 reduced-motion (freezes all drift)
//   pulse  - transient accent wash (level-up), decays via tween

uniform vec4 top_color : source_color = vec4(0.039, 0.043, 0.078, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.086, 0.094, 0.180, 1.0);
uniform float dim : hint_range(0.0, 1.0) = 0.0;
uniform float motion : hint_range(0.0, 1.0) = 1.0;
uniform vec4 pulse_color : source_color = vec4(0.20, 0.85, 1.0, 1.0);
uniform float pulse : hint_range(0.0, 1.0) = 0.0;
uniform vec2 size = vec2(720.0, 1280.0);

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float blob(vec2 px, vec2 center, float radius) {
    float t = clamp(1.0 - length(px - center) / radius, 0.0, 1.0);
    return t * t;
}

void fragment() {
    vec2 uv = UV;
    vec2 px = uv * size;
    float t = TIME * motion;

    vec3 col = mix(top_color.rgb, bottom_color.rgb, uv.y);

    // Aurora blobs — 60-120s drift periods, additive at low intensity.
    float blob_gain = 1.0 - dim * 0.70;
    vec2 c1 = size * vec2(0.25 + 0.10 * sin(t * 0.052), 0.28 + 0.08 * cos(t * 0.037));
    vec2 c2 = size * vec2(0.80 + 0.08 * sin(t * 0.043 + 2.0), 0.62 + 0.10 * cos(t * 0.031 + 1.0));
    vec2 c3 = size * vec2(0.50 + 0.12 * sin(t * 0.029 + 4.0), 0.95 + 0.06 * cos(t * 0.047 + 3.0));
    col += vec3(0.10, 0.36, 0.55) * blob(px, c1, size.x * 0.58) * 0.15 * blob_gain;
    col += vec3(0.30, 0.15, 0.55) * blob(px, c2, size.x * 0.62) * 0.14 * blob_gain;
    col += vec3(0.08, 0.18, 0.50) * blob(px, c3, size.x * 0.52) * 0.11 * blob_gain;

    // Faint dot grid.
    vec2 g = mod(px, 48.0) - vec2(24.0);
    float dot_m = smoothstep(2.2, 1.0, length(g));
    col += vec3(0.5, 0.7, 1.0) * dot_m * 0.022 * (1.0 - dim * 0.6);

    // Drifting specks (<10px/s), one per 160px cell. Off during gameplay.
    vec2 cell = floor(px / 160.0);
    vec2 rnd = vec2(hash(cell), hash(cell + 19.7));
    vec2 sp = (cell + rnd) * 160.0
            + vec2(sin(t * 0.08 + rnd.x * 6.28), cos(t * 0.06 + rnd.y * 6.28)) * 12.0;
    float speck = smoothstep(2.0, 0.5, length(px - sp));
    col += vec3(0.6, 0.8, 1.0) * speck * 0.10 * (1.0 - dim) * (0.4 + 0.6 * rnd.y);

    // Transient accent wash (level-up and similar moments).
    col += pulse_color.rgb * pulse * 0.12;

    // Vignette.
    vec2 v = uv - 0.5;
    col *= 1.0 - dot(v, v) * 0.55;

    // Dither: breaks up gradient banding on OLED.
    col += (hash(px) - 0.5) * (2.0 / 255.0);

    COLOR = vec4(col, 1.0);
}
