// The 3D viewport's backdrop, drawn as one full-screen triangle behind everything: a sky from horizon to zenith,
// the void below the horizon, and an infinite grid on the ground plane with the X and Y axes along it.
// Scene and the colour helpers come from scene.wgsl; the frame is dithered as it goes to the window (present.wgsl).

struct Backdrop {
    @builtin(position) position: vec4f,
    @location(0) ndc: vec2f,
};

@vertex
fn vs_backdrop(@builtin(vertex_index) index: u32) -> Backdrop {
    var corners = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
    var output: Backdrop;
    // Reversed Z: the backdrop sits at depth zero, behind everything.
    output.position = vec4f(corners[index], 0.0, 1.0);
    output.ndc = corners[index];
    return output;
}

// Coverage of the nearest line of a unit grid, about one pixel wide at any distance.
fn grid_line(coordinate: vec2f, derivative: vec2f) -> f32 {
    let distance_to_line = abs(fract(coordinate - 0.5) - 0.5) / max(derivative, vec2f(0.00001));
    return 1.0 - clamp(min(distance_to_line.x, distance_to_line.y), 0.0, 1.0);
}

// Coverage of the line where a coordinate is zero, a little wider than the grid's.
fn axis_line(value: f32, derivative: f32) -> f32 {
    return 1.0 - clamp(abs(value) / max(derivative * 1.5, 0.00001), 0.0, 1.0);
}

@fragment
fn fs_backdrop(input: Backdrop) -> @location(0) vec4f {
    let aspect = scene.projection.x;
    let tan_half = scene.projection.y;
    let ground = scene.projection.z;
    let spacing = scene.projection.w;
    let ray = normalize(scene.forward.xyz + scene.right.xyz * input.ndc.x * aspect * tan_half + scene.up.xyz * input.ndc.y * tan_half);

    // The sky brightens towards the horizon; below it, the void. A thin blend keeps the horizon from aliasing.
    let sky = mix(scene.horizon.rgb, scene.zenith.rgb, sqrt(clamp(ray.z, 0.0, 1.0)));
    var colour = mix(scene.void_colour.rgb, sky, smoothstep(-0.004, 0.004, ray.z));

    // Where the ray meets the ground plane. Derivatives need uniform control flow, so this runs for every pixel
    // and only the result is masked.
    let toward = select(ray.z, -1e-6, abs(ray.z) < 1e-6);
    let distance = (ground - scene.eye.z) / toward;
    let point = scene.eye.xyz + ray * distance;
    let cell = point.xy / spacing;
    let minor = grid_line(cell, fwidth(cell));
    let major = grid_line(cell * 0.1, fwidth(cell * 0.1));
    let axis_x = axis_line(point.y, fwidth(point.y));
    let axis_y = axis_line(point.x, fwidth(point.x));
    if (distance > 0.0) {
        // Faded with distance, and at grazing angles, where lines would shimmer.
        let fade = exp(-distance / (spacing * 400.0)) * smoothstep(0.0, 0.08, abs(ray.z));
        colour = mix(colour, scene.grid_minor.rgb, minor * scene.grid_minor.a * fade);
        colour = mix(colour, scene.grid_major.rgb, major * scene.grid_major.a * fade);
        colour = mix(colour, scene.axis_x.rgb, axis_x * scene.axis_x.a * fade);
        colour = mix(colour, scene.axis_y.rgb, axis_y * scene.axis_y.a * fade);
    }

    return vec4f(colour, 1.0);
}
