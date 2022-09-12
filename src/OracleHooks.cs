using Mono.Cecil.Cil;
using MonoMod.Cil;
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

        string text = Rng(0, 4) switch {
            0 => "It seems this pearl used to have data, but...<LINE>Has it been... charred? How did you manage this, <PlayerName>?",
            1 => "I can't discern any pattern from this.<LINE>If there was any information inside, it's too warped for me to parse.",
            2 => "Oh, <PlayerName>... You gave me a half-melted pearl.",
            _ => "The contents are scrambled far beyond recognition.<LINE>No doubt, it took an inordinate supply of energy to erase<LINE>any semblance of data so completely. And for what?",
        };
        events.Add(new TextEvent(this, 30, text, 10));
    }

    private void PearlIntro()
    {
        // Copied from SLOracleBehaviorHasMark.MoonConversation.PearlIntro()
        switch (oracle.State.totalPearlsBrought + oracle.State.miscPearlCounter) {
            case 0:
                events.Add(new TextEvent(this, 0, oracle.Translate("Ah, you would like me to read this?"), 10));
                events.Add(new TextEvent(this, 0, oracle.Translate("It's a bit dusty, but I will do my best. Hold on..."), 10));
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
                        events.Add(new TextEvent(this, 0, oracle.Translate("The scavengers must be jealous of you, finding all these"), 10));
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
        IL.SLOracleBehaviorHasMark.GrabObject += SLOracleBehaviorHasMark_GrabObject;
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
                return true;
            }
            return false;
        }
    }
}
