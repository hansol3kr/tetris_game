shader_type canvas_item;

// The animated backdrop — the largest cosmetic surface in the game. It paints behind every
// screen AND behind the play field, so it is what an equipped skin changes most visibly.
//
// One pass, no texture reads. Ten procedural SCENES selected by the `pattern` uniform
// (Blockfall.Theme.BackdropStyle in C#, ordinal for ordinal). A scene contributes MOTION AND
// FORM only; its palette is the skin's gradient (top/bottom_color) plus the skin's resolved UI
// accents (accent_a/accent_b), so N scenes × M skins read as N·M different backdrops instead of
// N wallpapers.
//
// Script-driven uniforms:
//   pattern       - which scene (0 = the shipped Aurora)
//   dim           - 0 on menus, 1 during gameplay (scene recedes; fine detail recedes harder)
//   motion        - 1 normal, 0 reduced-motion (freezes all drift)
//   pulse         - transient accent wash (level-up / equip flourish), decays via tween
//   top/bottom_color, accent_a, accent_b - the equipped skin's colours
//
// GATING RULES, structural rather than per-scene taste:
//   * `motion` multiplies TIME once, at the top. Every animated term is a function of `t`, so
//     reduced motion freezes all ten scenes with no per-scene branch to forget.
//   * `dim` splits into two gains. `vis` fades broad slow forms; `fine` fades high-frequency
//     detail (stars, rain, traces, embers) much harder, because that is the only thing on this
//     surface that can fight the HUD read-outs drawn straight on top of it during a run.
//   * Playfield legibility is NOT defended here — the board is its own near-opaque plane (see
//     BlockFitController.DrawUnder), which is exactly what lets this surface be interesting.
//   * Pattern 0 is the shipped Aurora, math verbatim, so an untouched save renders precisely
//     what it rendered before this file grew nine siblings.

uniform vec4 top_color : source_color = vec4(0.039, 0.043, 0.078, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.086, 0.094, 0.180, 1.0);
uniform vec4 accent_a : source_color = vec4(0.20, 0.85, 1.0, 1.0);
uniform vec4 accent_b : source_color = vec4(0.70, 0.42, 1.0, 1.0);
uniform int pattern : hint_range(0, 9) = 0;
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

// Value noise + 3-octave fbm. Only NEBULA samples this (12 hashes/px); every other scene is
// analytic, which keeps the long-tail mobile cost of this file bounded by one scene.
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float s = 0.0;
    float a = 0.5;
    for (int i = 0; i < 3; i++) {
        s += a * vnoise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return s;
}

// One drifting point layer: a single spark per `cell`-sized bucket, twinkling out of phase.
// Shared by NEBULA / STARFIELD / BOKEH / EMBERS so those scenes differ in shape and tint rather
// than in four copies of the same bookkeeping.
//
// SIGN OF `speed`: POSITIVE RISES. A spark sits where fract(pp/cell) == c, i.e. at
// px.y = (id.y + c.y)*cell - t*speed, so px.y DECREASES as t grows. (An earlier version of this
// note said the opposite and EMBERS was authored against it — its sparks fell into the hearth
// glow instead of rising off it, which is the whole idea of the scene.)
float sparkLayer(vec2 px, float cell, float t, float speed, float radius, float twinkle) {
    vec2 pp = px + vec2(0.0, t * speed);
    vec2 id = floor(pp / cell);
    vec2 f = fract(pp / cell);
    vec2 c = vec2(hash(id), hash(id + vec2(19.7, 4.3)));
    float d = length((f - c) * cell);
    float tw = 1.0 - twinkle + twinkle * (0.5 + 0.5 * sin(t * 2.2 + hash(id + vec2(7.1, 1.9)) * 6.283));
    return smoothstep(radius, 0.0, d) * tw;
}

void fragment() {
    vec2 uv = UV;
    vec2 px = uv * size;
    float t = TIME * motion;

    // Broad forms fade to 45% during a run; fine detail to 20%. See the gating note above.
    float vis = 1.0 - dim * 0.55;
    float fine = 1.0 - dim * 0.80;

    vec3 col = mix(top_color.rgb, bottom_color.rgb, uv.y);
    vec3 a = accent_a.rgb;
    vec3 b = accent_b.rgb;

    if (pattern == 0) {
        // ---- AURORA (the original NEON FLUX backdrop; math unchanged) -----------------
        float blob_gain = 1.0 - dim * 0.70;
        vec2 c1 = size * vec2(0.25 + 0.10 * sin(t * 0.052), 0.28 + 0.08 * cos(t * 0.037));
        vec2 c2 = size * vec2(0.80 + 0.08 * sin(t * 0.043 + 2.0), 0.62 + 0.10 * cos(t * 0.031 + 1.0));
        vec2 c3 = size * vec2(0.50 + 0.12 * sin(t * 0.029 + 4.0), 0.95 + 0.06 * cos(t * 0.047 + 3.0));
        col += vec3(0.10, 0.36, 0.55) * blob(px, c1, size.x * 0.58) * 0.15 * blob_gain;
        col += vec3(0.30, 0.15, 0.55) * blob(px, c2, size.x * 0.62) * 0.14 * blob_gain;
        col += vec3(0.08, 0.18, 0.50) * blob(px, c3, size.x * 0.52) * 0.11 * blob_gain;

        vec2 g = mod(px, 48.0) - vec2(24.0);
        float dot_m = smoothstep(2.2, 1.0, length(g));
        col += vec3(0.5, 0.7, 1.0) * dot_m * 0.022 * (1.0 - dim * 0.6);

        vec2 cell = floor(px / 160.0);
        vec2 rnd = vec2(hash(cell), hash(cell + 19.7));
        vec2 sp = (cell + rnd) * 160.0
                + vec2(sin(t * 0.08 + rnd.x * 6.28), cos(t * 0.06 + rnd.y * 6.28)) * 12.0;
        float speck = smoothstep(2.0, 0.5, length(px - sp));
        col += vec3(0.6, 0.8, 1.0) * speck * 0.10 * (1.0 - dim) * (0.4 + 0.6 * rnd.y);
    } else if (pattern == 1) {
        // ---- NEBULA: two fbm layers, the second warped by the first --------------------
        vec2 q = px / size.x;
        float n1 = fbm(q * 3.0 + vec2(t * 0.010, -t * 0.006));
        float n2 = fbm(q * 5.5 + vec2(-t * 0.008, t * 0.011) + n1 * 0.6);
        float cloud = smoothstep(0.30, 0.92, n1 * 0.65 + n2 * 0.50);
        col += mix(b, a, n2) * cloud * 0.20 * vis;
        // Pin stars punched through the gas so the depth reads.
        col += vec3(0.85, 0.90, 1.0) * sparkLayer(px, 190.0, t, 2.0, 1.7, 0.7) * 0.30 * fine;
    } else if (pattern == 2) {
        // ---- STARFIELD: three parallax layers, near ones bigger and faster ------------
        col += vec3(0.80, 0.86, 1.00) * sparkLayer(px, 210.0, t, 1.5, 1.4, 0.8) * 0.34 * fine;
        col += mix(vec3(1.0), a, 0.35) * sparkLayer(px, 130.0, t, 4.0, 1.9, 0.6) * 0.26 * fine;
        col += mix(vec3(1.0), b, 0.45) * sparkLayer(px, 76.0, t, 9.0, 2.6, 0.4) * 0.16 * fine;
        // The band: a soft diagonal wash of unresolved stars.
        float band = exp(-abs((uv.y - 0.42) - (uv.x - 0.5) * 0.28) * 7.0);
        col += mix(a, b, 0.5) * band * 0.055 * vis;
    } else if (pattern == 3) {
        // ---- GRID: a floor receding to a lit horizon ----------------------------------
        // Lines FADE toward the horizon (smoothstep on z) rather than converging into a
        // fract() moiré — that aliasing is what makes cheap perspective grids look broken.
        float hz = 0.46;
        float z = uv.y - hz;
        float below = step(0.0, z);
        // 0.25, not 0.055: the numerator sets how many cells fit in the visible band, and at
        // 0.055 the fract() argument spanned well under one period across the whole floor — the
        // "grid" rendered as one fat centre wedge and a single horizontal line. Measured sweep at
        // 0.25: ~6 horizontal rungs bunching toward the horizon and verticals converging to the
        // centre line, i.e. actual perspective. Both axes read this, so it is one number.
        float depth = 0.25 / max(z, 0.0006);
        float gx = fract((uv.x - 0.5) * depth * 0.75);
        float gy = fract(depth + t * 0.22);
        float lx = smoothstep(0.055, 0.0, min(gx, 1.0 - gx));
        float ly = smoothstep(0.075, 0.0, min(gy, 1.0 - gy));
        float fade = smoothstep(0.0, 0.16, z) * (1.0 - smoothstep(0.42, 0.56, z));
        col += a * (lx * 0.85 + ly) * below * fade * 0.16 * vis;
        // Horizon bloom + the disc sitting on it.
        col += b * exp(-abs(uv.y - hz) * 44.0) * 0.22 * vis;
        float sun = smoothstep(0.20, 0.0, length((uv - vec2(0.5, hz - 0.06)) * vec2(1.0, 1.9)));
        col += mix(b, a, 0.35) * sun * 0.14 * vis;
    } else if (pattern == 4) {
        // ---- RAYS: a slow fan of light from a point above the screen -------------------
        vec2 o = vec2(0.5, -0.14) * size;
        vec2 d = px - o;
        float ang = atan(d.y, d.x);
        float r1 = 0.5 + 0.5 * sin(ang * 13.0 + t * 0.10);
        float r2 = 0.5 + 0.5 * sin(ang * 7.0 - t * 0.06 + 1.7);
        float falloff = exp(-length(d) / (size.y * 0.80));
        col += mix(a, b, 0.35) * pow(r1, 3.0) * falloff * 0.24 * vis;
        col += mix(b, a, 0.25) * pow(r2, 4.0) * falloff * 0.14 * vis;
    } else if (pattern == 5) {
        // ---- WAVES: four stacked swells + a caustic sheen ------------------------------
        float acc = 0.0;
        float sheen = 0.0;
        for (int i = 0; i < 4; i++) {
            float fi = float(i);
            float yb = 0.22 + fi * 0.21;
            float w = yb
                    + 0.030 * sin(uv.x * 5.5 + t * 0.28 + fi * 1.7)
                    + 0.016 * sin(uv.x * 11.0 - t * 0.21 + fi);
            acc += smoothstep(0.011, 0.0, abs(uv.y - w));
            sheen += exp(-abs(uv.y - w) * 26.0) * 0.35;
        }
        col += a * acc * 0.17 * vis;
        col += mix(a, b, 0.5) * sheen * 0.045 * vis;
    } else if (pattern == 6) {
        // ---- BOKEH: soft out-of-focus discs drifting up --------------------------------
        // Rings, not filled dots: an out-of-focus highlight is brighter at its rim.
        //
        // A disc's radius (up to 64px) is comparable to its own 150px hash bucket, so a centre
        // sitting near a bucket edge OVERHANGS into the neighbour — and evaluating only the
        // pixel's own bucket razor-cut the bright rim along the seam on most discs. Since
        // r_max (64) < half a cell (75), only the NEARER neighbour on each axis can possibly
        // reach this pixel, so a sign-selected 2×2 sweep is exact (bit-identical to a full 3×3
        // at four disc evaluations instead of nine).
        vec2 pp = px + vec2(0.0, t * 6.0);
        vec2 id = floor(pp / 150.0);
        vec2 f = fract(pp / 150.0);
        vec2 nb = vec2(f.x < 0.5 ? -1.0 : 1.0, f.y < 0.5 ? -1.0 : 1.0);
        vec2 offs[4] = { vec2(0.0, 0.0), vec2(nb.x, 0.0), vec2(0.0, nb.y), nb };
        for (int i = 0; i < 4; i++) {
            vec2 o = offs[i];
            vec2 nid = id + o;
            vec2 c = vec2(hash(nid), hash(nid + vec2(3.1, 8.6))) + o;
            float d = length((f - c) * 150.0) / (34.0 + 30.0 * hash(nid + vec2(5.5, 2.2)));
            float ring = smoothstep(1.0, 0.86, d) * (0.35 + 0.65 * smoothstep(0.55, 1.0, d));
            col += mix(a, b, hash(nid + vec2(11.3, 0.7))) * ring * 0.16 * vis;
        }
        col += mix(b, a, 0.4) * sparkLayer(px, 90.0, t, 3.0, 2.0, 0.5) * 0.10 * fine;
    } else if (pattern == 7) {
        // ---- RAIN: slanted streaks, one per column, each with its own speed ------------
        vec2 pp = px;
        pp.x += pp.y * 0.16;                                   // slant
        float colx = pp.x / 26.0;
        vec2 id = vec2(floor(colx), 0.0);
        float fx = fract(colx);
        float sp = 0.30 + 0.55 * hash(id);
        float ph = fract(hash(id + vec2(7.1, 0.0)) + t * sp);
        // Streak LENGTH has to be pixel-absolute like the 26px column pitch above it, or a small
        // surface squashes the drop vertically while its column stays 26px wide — on the 122px
        // store card that turned rain into squat dashes wider than they were tall, i.e. the card
        // lied about the scene it sells. `k` rescales the uv-tuned constants to the 1280-tall
        // surface they were authored on, so full-screen output is unchanged (k = 1 there).
        float k = size.y / 1280.0;
        float y = pp.y / size.y;
        float head = ph * 1.3 - 0.15;
        float tail = clamp((head - y) * 9.0 * k, 0.0, 1.0);    // the trail is only ABOVE the head
        float streak = exp(-abs(y - head) * 46.0 * k) * (0.25 + 0.75 * tail);
        streak *= smoothstep(0.5, 0.05, abs(fx - 0.5));
        col += mix(a, vec3(0.85, 0.92, 1.0), 0.45) * streak * 0.40 * fine;
        // Wet sheen pooling toward the bottom.
        col += a * smoothstep(0.55, 1.0, uv.y) * 0.030 * vis;
    } else if (pattern == 8) {
        // ---- EMBERS: warm sparks rising with a flicker ---------------------------------
        // Only 35% skin-tinted on purpose: a cyan ember is not an ember. The skin still owns
        // the gradient behind it, so the scene never fights the equipped palette.
        vec3 warm = mix(a, vec3(1.00, 0.55, 0.18), 0.65);
        col += warm * sparkLayer(px, 120.0, t, 22.0, 2.4, 0.85) * 0.42 * fine;
        col += mix(warm, vec3(1.0, 0.80, 0.45), 0.5) * sparkLayer(px, 210.0, t, 11.0, 1.6, 0.9) * 0.30 * fine;
        // Hearth glow from below.
        col += warm * pow(uv.y, 4.0) * 0.16 * vis;
    } else {
        // ---- CIRCUIT: orthogonal traces with travelling pulses -------------------------
        vec2 g = px / 64.0;
        vec2 id = floor(g);
        vec2 f = fract(g);
        float h = hash(id);
        float horiz = smoothstep(0.040, 0.0, abs(f.y - 0.5));
        float vert = smoothstep(0.040, 0.0, abs(f.x - 0.5));
        float trace = h < 0.5 ? horiz : vert;
        float along = h < 0.5 ? f.x : f.y;
        float ph = fract(t * 0.22 + h * 3.0);
        float spark = exp(-abs(along - ph) * 22.0);
        float pad = smoothstep(0.085, 0.0, length(f - vec2(0.5)));
        col += a * trace * 0.085 * fine;
        col += b * trace * spark * 0.55 * fine;
        col += a * pad * 0.13 * fine;
    }

    // Transient accent wash (level-up and similar moments).
    col += pulse_color.rgb * pulse * 0.12;

    // Vignette.
    vec2 v = uv - 0.5;
    col *= 1.0 - dot(v, v) * 0.55;

    // Dither: breaks up gradient banding on OLED.
    col += (hash(px) - 0.5) * (2.0 / 255.0);

    COLOR = vec4(col, 1.0);
}
