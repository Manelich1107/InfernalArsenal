using CalamityMod;
using InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Core.Players
{
    public enum TwoPathsSide
    {
        Dark,
        Light
    }

    public class TwoPathsPlayer : ModPlayer
    {
        public const float MaxEnergy = 100f;
        public const float DarkGainPerHit = 20f;
        public const float LightGainPerHit = 10f;
        public const float BalanceSeconds = 6f;
        public const float BalanceDrainPerTick = MaxEnergy / (BalanceSeconds * 60f);
        public const int DecayDelay = 300;
        public const float DecayPerTick = 0.4f;

        public float Dark;
        public float Light;
        public bool Balance;
        public TwoPathsSide LastSide = TwoPathsSide.Dark;

        private int nextAttackId = 1;
        private int lastCreditedDark;
        private int lastCreditedLight;
        private int ticksSinceCredit;

        public bool HoldingTwoPaths => Player.HeldItem.type == ModContent.ItemType<TwoPaths>();

        public int BeginAttack(TwoPathsSide side)
        {
            LastSide = side;
            return nextAttackId++;
        }

        // The balance spin's hits and projectiles never charge the gauges, even those landing after balance has ended.
        public int BeginBalanceAttack()
        {
            int attackId = nextAttackId++;
            lastCreditedDark = lastCreditedLight = attackId;
            return attackId;
        }

        // Balance is spent by the spin (TwoPathsBalanceSpin) while it turns: full gauges last BalanceSeconds.
        public void DrainBalance(float amount)
        {
            Dark = MathHelper.Max(Dark - amount, 0f);
            Light = MathHelper.Max(Light - amount, 0f);
        }

        public void CreditHit(TwoPathsSide side, int attackId)
        {
            if (attackId <= 0)
                return;

            // Hits keep the energy from decaying even in balance, where they no longer charge it.
            ticksSinceCredit = 0;
            if (Balance)
                return;
            if (side == TwoPathsSide.Dark)
            {
                if (attackId <= lastCreditedDark)
                    return;
                lastCreditedDark = attackId;
                Dark = MathHelper.Min(Dark + DarkGainPerHit, MaxEnergy);
            }
            else
            {
                if (attackId <= lastCreditedLight)
                    return;
                lastCreditedLight = attackId;
                Light = MathHelper.Min(Light + LightGainPerHit, MaxEnergy);
            }
        }

        public override void PostUpdate()
        {
            if (++ticksSinceCredit > DecayDelay)
            {
                Dark = MathHelper.Max(Dark - DecayPerTick, 0f);
                Light = MathHelper.Max(Light - DecayPerTick, 0f);
            }

            if (Balance)
            {
                if (Dark <= 0f && Light <= 0f)
                    Balance = false;
            }
            else if (Dark >= MaxEnergy && Light >= MaxEnergy)
            {
                Balance = true;
                if (Player.whoAmI == Main.myPlayer && !StartMerge())
                    BalanceEntryEffects();
            }

            // The front hand grips the scythe while it is held idle (drawn by TwoPathsHoldLayer).
            if (TwoPathsHoldLayer.ShowsIdleHold(Player))
                Player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, TwoPathsHoldLayer.ArmRotation(Player));
        }

        // Entering balance with the scythe in hand cuts the current swing short and starts the balance spin with its merge
        // intro; the spin plays the entry burst when the halves lock.
        private bool StartMerge()
        {
            if (!HoldingTwoPaths)
                return false;

            int dark = ModContent.ProjectileType<TwoPathsDarkHeld>();
            int light = ModContent.ProjectileType<TwoPathsLightHeld>();
            int spin = ModContent.ProjectileType<TwoPathsBalanceSpin>();
            foreach (Projectile projectile in Main.ActiveProjectiles)
            {
                if (projectile.owner == Player.whoAmI && (projectile.type == dark || projectile.type == light || projectile.type == spin))
                    projectile.Kill();
            }

            Vector2 aim = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX * Player.direction);
            Item item = Player.HeldItem;
            Projectile.NewProjectile(Player.GetSource_ItemUse(item), Player.MountedCenter, aim, spin, Player.GetWeaponDamage(item), Player.GetWeaponKnockback(item), Player.whoAmI, aim.X >= 0f ? 1f : -1f, 1f);
            return true;
        }

        public void BalanceEntryEffects()
        {
            SoundEngine.PlaySound(SoundID.Item29 with { Pitch = -0.4f, Volume = 0.9f }, Player.Center);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.5f, Volume = 0.5f }, Player.Center);
            SoundEngine.PlaySound(SoundID.DD2_EtherianPortalOpen with { Pitch = 0.2f, Volume = 0.5f }, Player.Center);

            for (int line = 0; line < 2; line++)
            {
                Vector2 axis = (line == 0 ? new Vector2(1f, -1f) : new Vector2(1f, 1f)).SafeNormalize(Vector2.UnitX);
                int dustType = line == 0 ? DustID.GoldFlame : DustID.Shadowflame;
                for (int i = 0; i < 48; i++)
                {
                    float along = MathHelper.Lerp(-120f, 120f, i / 47f);
                    Dust dust = Dust.NewDustPerfect(Player.Center + axis * along, dustType, axis.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-1.5f, 1.5f) + axis * along * 0.02f);
                    dust.noGravity = true;
                    dust.scale = Main.rand.NextFloat(1.2f, 1.8f);
                }
            }

            for (int i = 0; i < 60; i++)
            {
                Dust dust = Dust.NewDustPerfect(Player.Center, Main.rand.NextBool() ? DustID.GoldFlame : DustID.CrimsonTorch, Main.rand.NextVector2Unit() * Main.rand.NextFloat(4f, 10f));
                dust.noGravity = !Main.rand.NextBool(4);
                dust.scale = Main.rand.NextFloat(0.9f, 1.8f);
            }

            Player.SetScreenshake(2.5f);
        }

        public override void UpdateDead()
        {
            Dark = 0f;
            Light = 0f;
            Balance = false;
        }
    }
}
