// Red Dead Redemption 2's vertices as the viewer stores them, into RedFury's material input: what Rdr2/surface.wgsl (a model)
// and Rdr2/world.wgsl (the world's placements) share. Put after Rdr2/material.wgsl.

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
