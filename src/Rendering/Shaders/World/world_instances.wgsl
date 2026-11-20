// A world's placements, as each streamed asset's storage holds them (ViewportRenderer.World.cs), for every game's world
// shaders: each placement's world matrix, from the asset's own space, then its inverse transpose. An ordinary
// placement's n3.x carries its tint palette row (0 keeps the material's), and n0.w and n1.w its level of detail fade
// (Scene/light.wgsl's lod_fade_keeps); a plant's n3 its fade's start and end, 1, and its occlusion, and the w of n0 to n2 its
// colour.

struct WorldInstance {
    m0: vec4f, m1: vec4f, m2: vec4f, m3: vec4f,
    n0: vec4f, n1: vec4f, n2: vec4f, n3: vec4f,
};

@group(3) @binding(0) var<storage, read> world_instances: array<WorldInstance>;

fn instance_model(instance: WorldInstance) -> mat4x4f {
    return mat4x4f(instance.m0, instance.m1, instance.m2, instance.m3);
}

fn instance_normal_matrix(instance: WorldInstance) -> mat4x4f {
    return mat4x4f(instance.n0, instance.n1, instance.n2, vec4f(0.0, 0.0, 0.0, 1.0));
}

// -1 when the placement mirrors its asset, which flips its tangents' handedness.
fn instance_handedness(instance: WorldInstance) -> f32 {
    return sign(determinant(mat3x3f(instance.m0.xyz, instance.m1.xyz, instance.m2.xyz)));
}

fn is_grass(instance: WorldInstance) -> bool {
    return instance.n3.z > 0.5;
}

// An ordinary placement's level of detail fade: when it began (the fades' clock, Scene/light.wgsl) and which way, 1 in, -1
// out, 0 none.
fn instance_lod_fade(instance: WorldInstance) -> vec2f {
    if (is_grass(instance)) {
        return vec2f(0.0);
    }
    return vec2f(instance.n0.w, instance.n1.w);
}

// A plant shrinks to nothing between its fade's start and end, from the camera at origin.
fn grass_fade_from(instance: WorldInstance, origin: vec3f) -> f32 {
    if (!is_grass(instance)) {
        return 1.0;
    }
    let distance = length(instance.m3.xyz - origin);
    return 1.0 - smoothstep(instance.n3.x, max(instance.n3.x + 0.01, instance.n3.y), distance);
}

// An interior placement's room ambient (WorldCullSet's lanes): its artificial ambient from above and below, packed,
// its natural ambient multiplier, and one in w; nought outside.
fn instance_room(instance: WorldInstance) -> vec4f {
    if (is_grass(instance) || instance.n3.z > -0.5) {
        return vec4f(0.0);
    }
    return vec4f(instance.n3.y, instance.n3.w, -instance.n3.z - 1.0, 1.0);
}

// An ordinary placement's tint palette row; 0 keeps the material's.
fn instance_tint_row_of(instance: WorldInstance) -> i32 {
    if (is_grass(instance)) {
        return 0;
    }
    return i32(round(instance.n3.x));
}
