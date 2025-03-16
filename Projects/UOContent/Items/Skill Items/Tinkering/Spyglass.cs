using ModernUO.Serialization;
using Server.Mobiles;
using Server.Network;

namespace Server.Items;

[Flippable(0x14F5, 0x14F6)]
[SerializationGenerator(0, false)]
public partial class Spyglass : Item
{
    [Constructible]
    public Spyglass() : base(0x14F5) => Weight = 3.0;

    public override void OnDoubleClick(Mobile from)
    {
        // You peer into the heavens, seeking the moons...
        from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 1008155);

        from.NetState.SendMessageLocalizedAffix(
            from.Serial,
            from.Body,
            MessageType.Regular,
            0x3B2,
            3,
            1008146 + (int)Clock.GetMoonPhase(Map.Trammel, from.X, from.Y),
            "",
            AffixType.Prepend,
            "Trammel : "
        );

        from.NetState.SendMessageLocalizedAffix(
            from.Serial,
            from.Body,
            MessageType.Regular,
            0x3B2,
            3,
            1008146 + (int)Clock.GetMoonPhase(Map.Felucca, from.X, from.Y),
            "",
            AffixType.Prepend,
            "Felucca : "
        );
    }
}
