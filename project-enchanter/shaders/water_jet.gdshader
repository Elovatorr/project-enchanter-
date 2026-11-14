// Stylised water jet used by the water enchantment.
// Applied to the two concentric cylinders spawned by WandShotFactory: the
// bright inner core and the wider translucent sheath.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

// Water tint (RGB); alpha is driven by `opacity` and the procedural foam.
uniform vec4 color : source_color = vec4(0.25, 0.5, 1.0, 1.0);
uniform float opacity : hint_range(0.0, 1.0) = 0.8;
uniform float emission_strength : hint_range(0.0, 4.0) = 1.2;

// Flow animation.
uniform float flow_speed : hint_range(0.0, 10.0) = 3.0;
uniform float streak_scale : hint_range(1.0, 24.0) = 8.0;
uniform float foam : hint_range(0.0, 1.0) = 0.65;

// Surface look.
uniform float edge_glow : hint_range(0.0, 2.0) = 0.9;
uniform float wobble : hint_range(0.0, 0.08) = 0.012;
uniform float core_boost : hint_range(0.0, 1.0) = 0.0;

// Cheap value noise (3-octave fbm) for flowing streaks and foam.
float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float value = 0.0;
	float amplitude = 0.5;
	for (int i = 0; i < 3; i++) {
		value += value_noise(p) * amplitude;
		p *= 2.0;
		amplitude *= 0.5;
	}
	return value;
}

void vertex() {
	// Gentle whip that grows toward the far tip (UV.y = 1) so the jet feels
	// like moving water instead of a rigid tube.
	float tip = UV.y;
	float phase = UV.y * 9.0 - TIME * flow_speed;
	VERTEX.x += sin(phase) * wobble * tip;
	VERTEX.z += cos(phase * 0.7) * wobble * tip;
}

void fragment() {
	// UV.x wraps around the cylinder; UV.y runs from the muzzle (0) to the tip (1).
	vec2 flow_uv = vec2(UV.x * 2.0, UV.y * 3.0 - TIME * flow_speed * 0.35);
	float streaks = fbm(vec2(flow_uv.x * streak_scale, flow_uv.y * 2.0));
	float fine = value_noise(vec2(UV.x * 26.0, UV.y * 18.0 - TIME * flow_speed * 1.4));
	float foam_mask = smoothstep(0.55, 0.85, streaks * 0.7 + fine * 0.3) * foam;

	// A water tube reads as round because its silhouette (grazing angles) is
	// brighter than the face-on centre: view-space fresnel rim.
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0) * edge_glow;

	// Dissolve at the far tip, soften the muzzle so it blends with the wand.
	float tip_fade = 1.0 - smoothstep(0.72, 1.0, UV.y);
	float muzzle_fade = smoothstep(0.0, 0.06, UV.y);

	vec3 water = color.rgb;
	ALBEDO = mix(water * 0.55, water + vec3(0.35), foam_mask);
	EMISSION = water * emission_strength * (0.45 + rim * 0.9 + foam_mask * 1.2)
		+ vec3(core_boost);
	ALPHA = clamp(opacity * (0.30 + rim + foam_mask * 0.9) * tip_fade * muzzle_fade, 0.0, 1.0);
}
