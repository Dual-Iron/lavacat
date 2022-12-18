using Mono.Cecil.Cil;
using MonoMod.Cil;
using SlugBase;
using static LavaCat.Extensions;

namespace LavaCat;

sealed class BurntPearlConvo : Conversation
{
    private readonly SLOracleBehaviorHasMark oracle;

    public BurntPearlConvo(SLOracleBehaviorHasMark oracle) : base(oracle, (ID)(-9472), oracle.dialogBox)
    {
        this.oracle = oracle;

        AddEvents();
    }

    public override void AddEvents()
    {
        PearlIntro();

        if (!oracle.oracle.room.game.TryGetSave(out LavaCatSaveState save)) {
            events.Add(new TextEvent(this, 0, "Oh. It seems SlugBase encountered an error,<LINE>so I can't read this pearl to you, <PlayerName>. I apologize.", 0));
            return;
        }

        string text = save.burntPearls switch {
            0 => "It seems this pearl used to have data, but... it is unreadable, now.<LINE>No doubt, it took an inordinate supply of energy to damage the pearl like this.",
            1 => "This kind of damage is familiar. Has it been... charred?<LINE>Did you find it like this, <PlayerName>?",
            2 => "Oh, <PlayerName>... You gave me another half-melted pearl.",
            3 => "Ah. Again, it is burnt all the way through. You must be the one doing this.<LINE>Once destroyed in this way, there is no way to recover the contents, you know.",
            4 => "... Please, <PlayerName>, try bringing me a pearl that is not burnt.",
            _ => "...",
        };
        events.Add(new TextEvent(this, 30, text, 20));
    }

    private void PearlIntro()
    {
        // Copied from SLOracleBehaviorHasMark.MoonConversation.PearlIntro()
        switch (oracle.State.totalPearlsBrought + oracle.State.miscPearlCounter) {
            case 0:
                events.Add(new TextEvent(this, 0, oracle.Translate("Ah, you would like me to read this?"), 10));
                events.Add(new TextEvent(this, 0, "It's a bit ashy, but I will do my best. Hold on...", 10));
                break;
            case 1:
                events.Add(new TextEvent(this, 0, oracle.Translate("Another pearl! You want me to read this one too? Just a moment..."), 10));
                break;
            case 2:
                events.Add(new TextEvent(this, 0, oracle.Translate("And yet another one! I will read it to you."), 10));
                break;
            case 3:
                events.Add(new TextEvent(this, 0, oracle.Translate("Another? You're no better than the scavengers!"), 10));
                if (oracle.State.GetOpinion == SLOrcacleState.PlayerOpinion.Likes) {
                    events.Add(new TextEvent(this, 0, oracle.Translate("Let us see... to be honest, I'm as curious to see it as you are."), 10));
                }
                break;
            default:
                switch (Rng(0, 5)) {
                    case 0:
                        break;
                    case 1:
                        events.Add(new TextEvent(this, 0, oracle.Translate("The scavengers must be jealous of you, finding all these."), 10));
                        break;
                    case 2:
                        events.Add(new TextEvent(this, 0, oracle.Translate("Here we go again, little archeologist. Let's read your pearl."), 10));
                        break;
                    case 3:
                        events.Add(new TextEvent(this, 0, oracle.Translate("... You're getting quite good at this you know. A little archeologist beast.<LINE>Now, let's see what it says."), 10));
                        break;
                    default:
                        events.Add(new TextEvent(this, 0, oracle.Translate("And yet another one! I will read it to you."), 10));
                        break;
                }
                break;
        }
    }
}

static class OracleHooks
{
    // TODO custom 5p and lttm dialogue
    public static void Apply()
    {
        On.SLOracleBehaviorHasMark.WillingToInspectItem += SLOracleBehaviorHasMark_WillingToInspectItem;
        IL.SLOracleBehaviorHasMark.GrabObject += SLOracleBehaviorHasMark_GrabObject;

        On.SSOracleBehavior.PebblesConversation.AddEvents += PebblesConversation_AddEvents;
    }

    private static void PebblesConversation_AddEvents(On.SSOracleBehavior.PebblesConversation.orig_AddEvents orig, SSOracleBehavior.PebblesConversation self)
    {
        if (self.owner.player.IsLavaCat()) {
            // TODO 5p
            // return;
        }

        orig(self);
    }

    private static bool SLOracleBehaviorHasMark_WillingToInspectItem(On.SLOracleBehaviorHasMark.orig_WillingToInspectItem orig, SLOracleBehaviorHasMark self, PhysicalObject item)
    {
        if (item is DataPearl pearl && pearl.AbstractPearl.dataPearlType == BurntPearl) {
            // Don't read burnt pearls after the fifth one
            if (self.oracle.room.game.TryGetSave(out LavaCatSaveState save) && save.burntPearls >= 5) {
                return false;
            }
        }
        return true;
    }

    private static void SLOracleBehaviorHasMark_GrabObject(ILContext il)
    {
        ILCursor cursor = new(il);

        cursor.GotoNext(MoveType.After, i => i.MatchIsinst<SSOracleSwarmer>());

        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldarg_1);
        cursor.EmitDelegate(SkipVanillaCode);

        static bool SkipVanillaCode(SSOracleSwarmer swarmer, SLOracleBehaviorHasMark self, PhysicalObject item)
        {
            if (item is DataPearl pearl && pearl.AbstractPearl.dataPearlType == BurntPearl && !self.State.HaveIAlreadyDescribedThisItem(item.abstractPhysicalObject.ID)) {
                self.currentConversation = new BurntPearlConvo(self);
                self.State.totalPearlsBrought++;
                self.State.totalItemsBrought++;
                self.State.AddItemToAlreadyTalkedAbout(item.abstractPhysicalObject.ID);

                if (self.oracle.room.game.TryGetSave(out LavaCatSaveState save)) {
                    save.burntPearls++;
                }
                return true;
            }
            return false;
        }
    }
}
