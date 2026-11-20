// Red Dead Redemption 2's drawables in the viewer: its vertices into RedFury's material (Rdr2/material.wgsl), in the
// main view and into the reflection map, as Gta5/surface.wgsl draws GTA V's. Put after scene.wgsl, light.wgsl,
// Rdr2/light.wgsl, Rdr2/terrain_blending.wgsl, Rdr2/material.wgsl and Rdr2/vertex.wgsl.

// RedFury's vs_material: positions are relative to the scene's origin already.
@vertex
fn vs_rdr2(vertex: Rdr2Vertex) -> MaterialVertexOutput {
    var output = material_vertex(rdr2_input(vertex));
    output.relative_position = vertex.position;
    output.position = scene.view_projection * vec4f(vertex.position, 1.0);
    return output;
}

// Into the reflection map, as Gta5/surface.wgsl's vs_surface_paraboloid: the hemisphere around the eye, the distance as depth.
struct Rdr2ReflectionView {
    eye: vec4f,
    side: vec4f,
};

@group(3) @binding(0) var<uniform> reflection_view: Rdr2ReflectionView;

@vertex
fn vs_rdr2_paraboloid(vertex: Rdr2Vertex) -> MaterialVertexOutput {
    var output = material_vertex(rdr2_input(vertex));
    output.relative_position = vertex.position;
    let offset = vertex.position - reflection_view.eye.xyz;
    let distance = max(length(offset), 1e-5);
    let d = offset / distance;
    let side = reflection_view.side.x;
    let z = d.z * side;
    let p = d.xy / max(1.0 + z, 1e-3);
    output.position = vec4f(p.x * side, p.y, saturate(distance / reflection_view.eye.w), 1.0);
    output.hemisphere = z;
    return output;
}

@fragment
fn fs_rdr2(input: MaterialVertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    use_local_lights(input.position.xy, input.relative_position);
    sub_sampled_view = true;
    return shade_material(input, front);
}

// The paraboloid mirrors the winding; what is behind the hemisphere is left out, after shading.
@fragment
fn fs_rdr2_reflected(input: MaterialVertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let colour = shade_material(input, !front);
    if (input.hemisphere < 0.0) {
        discard;
    }
    return colour;
}
