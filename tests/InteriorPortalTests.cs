using FiveFury.Ytyp;
using HikariHopper.Core;

namespace HikariHopper.Tests;

/// <summary>
/// Which rooms of a GTA V interior the outside sees into, through its portals (<see cref="GtaWorldCatalog.SeenFromOutside"/>),
/// on interiors made here: a portal is seen through from its room-from to its room-to, and back unless it's one-way;
/// a portal from the limbo (room nought, the outside) is turned round, as the game does, to lead from its room out.
/// </summary>
public sealed class InteriorPortalTests
{
    [Fact]
    public void A_one_way_portal_out_lets_its_room_look_out_but_not_the_outside_in()
    {
        // Written either way round: room-from the room and room-to the limbo, or room-from the limbo.
        foreach (bool fromLimbo in new[] { false, true })
        {
            MloArchetypeDef interior = Interior(out MloRoomDef[] rooms, "limbo", "office");
            Portal(interior, fromLimbo ? rooms[0] : rooms[1], fromLimbo ? rooms[1] : rooms[0], PortalFlags.OneWay);

            Assert.Equal(["limbo"], Seen(interior));
        }
    }

    [Fact]
    public void A_one_way_portal_between_rooms_is_seen_through_from_its_room_from_only()
    {
        MloArchetypeDef forwards = Interior(out MloRoomDef[] rooms, "limbo", "hall", "office");
        Portal(forwards, rooms[0], rooms[1]);
        Portal(forwards, rooms[1], rooms[2], PortalFlags.OneWay);
        Assert.Equal(["hall", "limbo", "office"], Seen(forwards));

        MloArchetypeDef backwards = Interior(out rooms, "limbo", "hall", "office");
        Portal(backwards, rooms[0], rooms[1]);
        Portal(backwards, rooms[2], rooms[1], PortalFlags.OneWay);
        Assert.Equal(["hall", "limbo"], Seen(backwards));
    }

    [Fact]
    public void A_two_way_portal_is_seen_through_either_way_round()
    {
        foreach (bool fromLimbo in new[] { false, true })
        {
            MloArchetypeDef interior = Interior(out MloRoomDef[] rooms, "limbo", "shop", "back");
            Portal(interior, fromLimbo ? rooms[0] : rooms[1], fromLimbo ? rooms[1] : rooms[0]);
            Portal(interior, rooms[2], rooms[1]);

            Assert.Equal(["back", "limbo", "shop"], Seen(interior));
        }
    }

    [Fact]
    public void Rooms_in_a_chain_are_seen_to_its_end()
    {
        MloArchetypeDef interior = Interior(out MloRoomDef[] rooms, "limbo", "a", "b", "c", "d");
        // Listed out of order, so it takes more than one pass over the portals.
        Portal(interior, rooms[3], rooms[4]);
        Portal(interior, rooms[2], rooms[3]);
        Portal(interior, rooms[1], rooms[2]);
        Portal(interior, rooms[1], rooms[0]);

        Assert.Equal(["a", "b", "c", "d", "limbo"], Seen(interior));
    }

    [Fact]
    public void A_cycle_of_rooms_is_seen_once_round()
    {
        MloArchetypeDef interior = Interior(out MloRoomDef[] rooms, "limbo", "a", "b", "c");
        Portal(interior, rooms[0], rooms[1]);
        Portal(interior, rooms[1], rooms[2]);
        Portal(interior, rooms[2], rooms[3]);
        Portal(interior, rooms[3], rooms[1]);

        Assert.Equal(["a", "b", "c", "limbo"], Seen(interior));
    }

    [Fact]
    public void A_room_no_portal_reaches_from_outside_is_not_seen()
    {
        MloArchetypeDef interior = Interior(out MloRoomDef[] rooms, "limbo", "lobby", "vault", "cell", "set");
        Portal(interior, rooms[0], rooms[1]);
        // Two rooms joined only to each other, and one that only looks out.
        Portal(interior, rooms[2], rooms[3]);
        Portal(interior, rooms[4], rooms[0], PortalFlags.OneWay);

        Assert.Equal(["limbo", "lobby"], Seen(interior));
    }

    [Fact]
    public void An_interior_without_rooms_has_none_to_leave_out()
    {
        Assert.Empty(GtaWorldCatalog.SeenFromOutside(new MloArchetypeDef()));
    }

    private static MloArchetypeDef Interior(out MloRoomDef[] rooms, params string[] names)
    {
        var interior = new MloArchetypeDef();
        rooms = [.. names.Select(name => new MloRoomDef { Name = name })];
        interior.Rooms.AddRange(rooms);
        return interior;
    }

    private static void Portal(MloArchetypeDef interior, MloRoomDef from, MloRoomDef to, PortalFlags flags = PortalFlags.None) =>
        interior.Portals.Add(new MloPortalDef { RoomFrom = from, RoomTo = to, Flags = flags });

    private static string[] Seen(MloArchetypeDef interior) =>
        [.. GtaWorldCatalog.SeenFromOutside(interior).Select(room => room.Name).Order(StringComparer.Ordinal)];
}
