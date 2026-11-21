// Red Dead Redemption 2's water: RedFury.Rendering's water_material.wgsl, copied, its camera, light and scene copy the
// viewer's (World/water_common.wgsl, Rdr2/light.wgsl). As RedFury says: the game's simulated normal and reflection buffers
// aren't assets, so the ripples are analytic, the reflection screen-space over the reflection map, and the refraction
// the opaque scene behind, without displacement. Its material's fog colour, ripple scale and bumpiness, flow and fog
// textures, foam and opacity are the game's. Put after Rdr2/material.wgsl, Rdr2/world.wgsl and World/water_common.wgsl.

// Integer-hashed unit gradient per lattice cell; stable at large world coordinates, unlike sin-based hashes.
fn water_gradient(cell: vec2f) -> vec2f {
    var h = vec2u(vec2i(cell) + vec2i(32768)) * vec2u(1597334673u, 3812015801u);
    var n = (h.x ^ h.y) * 1597334673u;
    n = (n ^ (n >> 16u)) * 2246822519u;
    let angle = f32(n >> 8u) * (6.28318530718 / 16777216.0);
    return vec2f(cos(angle), sin(angle));
}

// Quintic gradient noise; xy is the analytic derivative.
fn water_noise_derivative(p: vec2f) -> vec2f {
    let i = floor(p);
    let f = p - i;
    let u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    let du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
    let ga = water_gradient(i);
    let gb = water_gradient(i + vec2f(1.0, 0.0));
    let gc = water_gradient(i + vec2f(0.0, 1.0));
    let gd = water_gradient(i + vec2f(1.0, 1.0));
    let va = dot(ga, f);
    let vb = dot(gb, f - vec2f(1.0, 0.0));
    let vc = dot(gc, f - vec2f(0.0, 1.0));
    let vd = dot(gd, f - vec2f(1.0, 1.0));
    return ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd)
        + du * (u.yx * (va - vb - vc + vd) + vec2f(vb, vc) - va);
}

// Surface slope from rotated octaves of gradient noise: no repeating plane waves, so no stripes.
// Each octave fades once a pixel covers about half its wavelength, leaving a calm, stable far field.
fn water_ripple_slope(uv: vec2f, footprint: f32) -> vec2f {
    var result = vec2f(0.0);
    var p = uv * 0.5;
    var frequency = 0.5;
    var amplitude = 0.045;
    let rotation = mat2x2f(0.8, 0.6, -0.6, 0.8);
    for (var i = 0u; i < 6u; i++) {
        let attenuation = exp(-pow(footprint * frequency * 2.0, 2.0));
        result += water_noise_derivative(p + vec2f(f32(i) * 17.31, f32(i) * 9.17)) * amplitude * attenuation;
        p = rotation * p * 2.03;
        frequency *= 2.03;
        amplitude *= 0.72;
    }
    return result;
}

// Bounded screen-space ray march with perspective-correct depth and binary hit refinement.
// Off-screen/disoccluded hits fade to the sky. Only opaque scene geometry can be reflected. p and the hit are
// relative to the scene's origin, where the view projection takes them.
fn water_scene_reflection(p: vec3f, n: vec3f, direction: vec3f, fallback: vec3f) -> vec3f {
    // A single-layer screen depth cannot resolve distant thin trees consistently.
    // Fade local reflections before that undersampled regime instead of showing striped
    // silhouettes; the continuous sky reflection remains at every distance.
    let distance_fade = 1.0 - smoothstep(80.0, 180.0, length(p - rdr2_eye()));
    if (distance_fade <= 0.0) { return fallback; }
    let near = light.water.z;
    let origin = p + n * 0.08;
    let clip0 = scene.view_projection * vec4f(origin, 1.0);
    let rate = dot(direction, scene.forward.xyz);
    var ray_length = min(200.0, max(40.0, clip0.w * 2.0));
    if (rate < 0.0) { ray_length = min(ray_length, (near - clip0.w) / rate * 0.98); }
    if (ray_length <= 0.1 || clip0.w <= near) { return fallback; }
    let clip1 = scene.view_projection * vec4f(origin + direction * ray_length, 1.0);
    let uv0 = clip0.xy / clip0.w * vec2f(0.5, -0.5) + vec2f(0.5);
    let uv1 = clip1.xy / clip1.w * vec2f(0.5, -0.5) + vec2f(0.5);
    let steps = clamp(ceil(length((uv1 - uv0) * water_viewport()) / 4.0), 1.0, 40.0);
    var previous = 0.0;
    for (var i = 1.0; i <= steps; i += 1.0) {
        let t = i / steps;
        let uv = mix(uv0, uv1, t);
        if (any(uv <= vec2f(0.001)) || any(uv >= vec2f(0.999))) { break; }
        let scene_depth = water_depth_at(vec2i(uv * water_viewport()));
        let ray_distance = 1.0 / mix(1.0 / clip0.w, 1.0 / clip1.w, t);
        if (scene_depth > 0.0 && ray_distance >= water_view_distance(scene_depth)) {
            var lo = previous; var hi = t;
            for (var b = 0; b < 6; b++) {
                let mid = (lo + hi) * 0.5;
                let q = mix(uv0, uv1, mid);
                let d = water_depth_at(vec2i(q * water_viewport()));
                if (d > 0.0 && 1.0 / mix(1.0 / clip0.w, 1.0 / clip1.w, mid) >= water_view_distance(d)) { hi = mid; }
                else { lo = mid; }
            }
            let hit_uv = mix(uv0, uv1, hi);
            let hit_depth = water_depth_at(vec2i(hit_uv * water_viewport()));
            let hit_distance = water_view_distance(hit_depth);
            let gap = 1.0 / mix(1.0 / clip0.w, 1.0 / clip1.w, hi) - hit_distance;
            let hit = water_point(hit_uv * water_viewport(), hit_depth);
            // Do not reflect the riverbed or a discontinuous silhouette as an above-water object.
            if (gap >= 0.0 && gap < max(0.2, hit_distance * 0.002) && dot(hit - origin, n) > 0.08) {
                let edge = min(min(hit_uv.x, hit_uv.y), min(1.0 - hit_uv.x, 1.0 - hit_uv.y));
                let confidence = distance_fade * smoothstep(0.0, 0.08, edge) * (1.0 - smoothstep(0.8, 1.0, hi));
                let colour = water_background_at(vec2i(hit_uv * water_viewport()));
                return mix(fallback, colour, confidence);
            }
            return fallback;
        }
        previous = t;
    }
    return fallback;
}

fn shade_water(input: MaterialVertexOutput, front_facing: bool) -> vec4f {
    let flags = u32(material.water_settings.w);
    let kind = u32(material.water_settings.z);
    var fog = material.water_colour;
    if ((flags & 2u) != 0u) {
        fog = textureSample(terrain_tint_texture, clamp_sampler, input.uv);
        fog = vec4f(fog.rgb * fog.rgb, fog.a);
    }
    var flow_sample = vec4f(0.575, 0.54, 0.0, 0.0);
    if ((flags & 1u) != 0u) {
        flow_sample = textureSample(control_texture, clamp_sampler, input.uv);
    }
    var flow = flow_sample.rg;
    if (material.water_detail.y > 0.5) { flow = mix(input.uv1, flow, clamp(input.uv2.x, 0.0, 1.0)); }
    flow = flow * 2.0 - 1.0;
    let time = light.water.x;
    // The world's own coordinates, so the ripples stay put as the camera moves.
    let world = input.world_position + scene.origin.xyz;
    var uv = world.xy * material.water_settings.y;
    if (kind == 3u) { uv = input.uv * material.water_settings.y; flow = vec2f(0.0, -1.0); }
    if (material.water_detail.y > 0.5) { uv = mix(world.xy * input.uv2.y, uv, clamp(input.uv2.x, 0.0, 1.0)); }
    // Two alternating advection phases prevent a varying flow map from stretching indefinitely.
    let phase0 = fract(time * 0.08);
    let phase1 = fract(time * 0.08 + 0.5);
    let phase_blend = abs(phase0 * 2.0 - 1.0);
    let drift = flow + vec2f(0.08, 0.025);
    let footprint = max(length(dpdx(uv)), length(dpdy(uv)));
    let slope = mix(water_ripple_slope(uv - drift * phase0 * 3.0, footprint),
        water_ripple_slope(uv - drift * phase1 * 3.0 + vec2f(0.5), footprint), phase_blend) * material.water_settings.x;
    let geometric = normalize(input.normal) * select(-1.0, 1.0, front_facing);
    var tangent = normalize(input.tangent);
    var bitangent = normalize(input.bitangent);
    if (abs(geometric.z) > 0.5) {
        tangent = normalize(vec3f(1.0, 0.0, 0.0) - geometric * geometric.x);
        bitangent = normalize(cross(geometric, tangent));
    }
    let n = normalize(geometric + tangent * slope.x + bitangent * slope.y);
    let v = normalize(rdr2_eye() - input.world_position);
    let ndotv = clamp(dot(n, v), 0.001, 1.0);
    let pixel = vec2i(input.position.xy);
    let surface_distance = water_view_distance(input.position.z);
    let original_depth = water_depth_at(pixel);
    var thickness = max(water_view_distance(original_depth) - surface_distance, 0.0);
    // Isolated water meshes have no bottom in the scene; use a finite optical column.
    if (original_depth == 0.0) { thickness = 4.0; }
    let offset = vec2f(dot(n - geometric, scene.right.xyz), -dot(n - geometric, scene.up.xyz));
    var refracted_pixel = pixel + vec2i(offset * min(thickness * 30.0, 14.0));
    if (water_depth_at(refracted_pixel) > input.position.z) { refracted_pixel = pixel; }
    var background = water_background_at(refracted_pixel);
    // Nothing behind (the copy holds no sky, which the tone map adds later): the sky the refracted view meets.
    if (original_depth == 0.0) { background = rdr2_sky(refract(-v, n, 0.75), 0.0); }
    let density = max(fog.a * select(1.0, clamp(input.colour.r, 0.0, 1.0), kind == 2u), 0.0);
    var opacity = 0.0;
    if (kind == 2u) { opacity = mix(input.colour.g, material.water_opacity_override.a, flow_sample.a); }
    // Native G/override opacity enters the exponential transmission term, not output coverage.
    let absorption = (vec3f(1.0) - clamp(fog.rgb, vec3f(0.0), vec3f(0.95))) * (density * 3.0 + opacity * opacity);
    let transmission = exp(-absorption * min(thickness, 80.0));
    let l = normalize(light.sun_direction.xyz);
    let ndotl = max(dot(n, l), 0.0);
    let h = normalize(l + v);
    let f0 = vec3f(0.02037); // Air/water interface; viewer physical approximation.
    let fresnel = f0 + (vec3f(1.0) - f0) * pow(1.0 - ndotv, 5.0);
    let reflected_direction = reflect(-v, n);
    let sky = rdr2_sky(reflected_direction, 0.0);
    let reflection = water_scene_reflection(input.world_position, geometric, reflected_direction, sky);
    let shadow = sun_shadow(input.world_position, geometric);
    // Unresolved ripples widen the sun glint with distance instead of aliasing into sparkles.
    let roughness = clamp(0.1 + length(fwidth(world)) * 0.015, 0.1, 0.35);
    let specular = distribution_ggx(n, h, roughness) * visibility_smith_schlick(ndotv, ndotl, roughness)
        * fresnel_schlick(max(dot(h, v), 0.0), f0) * ndotl * shadow * rdr2_sun();
    // Light scattered back out of the water column: sky and direct sun (or moon) entering the surface.
    let sun_in = max(l.z, 0.0) * shadow * rdr2_sun() / PI;
    let scatter = fog.rgb * (sky_irradiance(vec3f(0.0, 0.0, 1.0)) + sun_in);
    var foam = 0.0;
    if ((flags & 4u) != 0u) {
        let foam_uv = world.xy * material.water_detail.x;
        let a = textureSampleGrad(occlusion_texture, material_sampler, foam_uv - flow * phase0 * 2.0, dpdx(foam_uv), dpdy(foam_uv)).r;
        let b = textureSampleGrad(occlusion_texture, material_sampler, foam_uv - flow * phase1 * 2.0 + vec2f(0.5), dpdx(foam_uv), dpdy(foam_uv)).r;
        // FlowTexture alpha is authored foam coverage, not the blue channel or vertex alpha.
        foam = flow_sample.a * smoothstep(0.25, 0.75, mix(a, b, phase_blend));
    }
    let foam_light = vec3f(0.65) * (sky_irradiance(geometric) + rdr2_sun() * ndotl * shadow / PI);
    let water_light = mix(reflection * fresnel + scatter * (vec3f(1.0) - transmission) * (vec3f(1.0) - fresnel) + specular, foam_light, foam);
    let retained = transmission * (vec3f(1.0) - fresnel) * (1.0 - foam);
    let coverage = smoothstep(0.0, 0.06, thickness);
    return vec4f(max(background * retained + water_light, vec3f(0.0)), coverage);
}

@fragment
fn fs_rdr2_world_water(input: MaterialVertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    return shade_water(input, front);
}
