// Red Dead Redemption 2's drawables in the viewer: its vertices into RedFury's material (rdr2_material.wgsl), in the
// main view and into the reflection map, as surface.wgsl draws GTA V's. Put after scene.wgsl, light.wgsl,
// rdr2_light.wgsl, rdr2_terrain_blending.wgsl and rdr2_material.wgsl.

// The viewer's surface vertex (SurfaceVertex): colour0 holds the colour RedFury's program reads (COLOR1 for the terrain
// programs that take it), colour1's red the tint palette's coordinate, and river water's COLOR1 rides in uv1 and uv2.
struct Rdr2Vertex {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(6) colour1: vec4f,
    @location(7) uv2: vec2f,
};

fn rdr2_input(vertex: Rdr2Vertex) -> MaterialVertexInput {
    var input: MaterialVertexInput;
    input.position = vertex.position;
    input.normal = vertex.normal;
    input.tangent = vertex.tangent;
    input.uv = vertex.uv0;
    input.uv1 = vertex.uv1;
    input.uv2 = vertex.uv2;
    input.colour = vertex.colour0;
    input.tint_palette_coordinate = vertex.colour1.r;
    return input;
}

// RedFury's vs_material: positions are relative to the scene's origin already.
@vertex
fn vs_rdr2(vertex: Rdr2Vertex) -> MaterialVertexOutput {
    var output = material_vertex(rdr2_input(vertex));
    output.relative_position = vertex.position;
    output.position = scene.view_projection * vec4f(vertex.position, 1.0);
    return output;
}

// Into the reflection map, as surface.wgsl's vs_surface_paraboloid: the hemisphere around the eye, the distance as depth.
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
