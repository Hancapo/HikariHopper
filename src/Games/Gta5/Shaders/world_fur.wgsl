// GTA V's fur grass in its world: Gta5/fur.wgsl's shells, each drawn on its own (the instances here are the placements),
// its material's copy saying which (fur.fade.z), and moved by its placement's matrix once pushed out. Faded out between
// furAlphaDistance's near and far as PS_FurDeferredTextured does: by the squared distance from the camera, its shade
// towards furLayerParams.w and its pixels left out as the fade falls, by a noise (the game's stipple texture read at a
// scrambled place). It writes what the world's other solid surfaces do.
// Put after Scene/scene.wgsl, Scene/light.wgsl, Gta5/fur.wgsl and World/world_instances.wgsl.

struct FurTargets {
    @location(0) colour: vec4f,
    @location(1) sub_sampled: f32,
    // And what the world's ambient occlusion takes back out (Scene/light.wgsl's occludable).
    @location(2) ambient: vec4f,
    @location(3) normal: vec4f,
};

@vertex
fn vs_fur_world(input: FurInput, @location(8) source_index: u32) -> FurOutput {
    let instance = world_instances[source_index];
    let model = instance_model(instance);
    var output = fur_vertex(input, u32(fur.fade.z + 0.5));
    output.place = (model * vec4f(output.place, 1.0)).xyz - scene.origin.xyz;
    output.normal = normalize((instance_normal_matrix(instance) * vec4f(output.normal, 0.0)).xyz);
    output.tangent = (model * vec4f(output.tangent, 0.0)).xyz;
    output.binormal = (model * vec4f(output.binormal, 0.0)).xyz * instance_handedness(instance);
    output.room = instance_room(instance);
    output.lod_fade = instance_lod_fade(instance);
    output.position = scene.view_projection * vec4f(output.place, 1.0);
    return output;
}

// Interleaved gradient noise over the frame's pixels, from nought to one.
fn fur_stipple(pixel: vec2f) -> f32 {
    return fract(52.9829189 * fract(dot(floor(pixel), vec2f(0.06711056, 0.00583715))));
}

@fragment
fn fs_fur_world(input: FurOutput, @builtin(front_facing) front: bool) -> FurTargets {
    use_local_lights(input.position.xy, input.place);
    use_room(input.room);
    let offset = input.place - (scene.eye.xyz - scene.origin.xyz);
    let near = fur.fade.x * fur.fade.x;
    let far = fur.fade.y * fur.fade.y;
    let fade = saturate((far - dot(offset, offset)) / max(far - near, 1e-4));
    // Left out after shading, which needs uniform control flow.
    let colour = shade_fur(input, front, fade);
    if (fade <= fur_stipple(input.position.xy) || !lod_fade_keeps(input.position.xy, input.lod_fade)) {
        discard;
    }
    return FurTargets(colour, 0.0, occludable_ambient, occludable_normal);
}
