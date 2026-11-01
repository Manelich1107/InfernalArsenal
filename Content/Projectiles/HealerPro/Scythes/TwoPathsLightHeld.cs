using System;
using System.IO;
using InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod.Buffs.Healer;
using ThoriumMod.Projectiles.Scythe;
using ThoriumMod.Utilities;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // Right click: aimed countdown swing (wind-up follows the cursor, the swing is locked).
    // ai[0] = attack time, ai[1] = countdown; Projectile.direction alternates the swing side.
    public class TwoPathsLightHeld : ScythePro
    {
        private const float SlashDistance = 480f;
        private const float RiftLength = 768f;
        private const int FlipTime = 6;
        private const int SoulCharge = 4;
        private const int TrailLength = 7;
        private const int GhostCount = 4;
        private const float TrailAngle = 2.3f;
        private const float StopTime = 0.07f;
        private const float SmearLinger = 0.25f;

        private bool shouldSpin;
        private bool mirror;
        private int attackId;
        private int flipTimer;
        private readonly float[] trail = new float[TrailLength];
        private int trailCount;

        public override string Texture => TwoPathsHelper.TexturePath;

        private float Progress => Projectile.ai[1] / Projectile.ai[0];
        private float BurstEnd => shouldSpin ? 0.72f : 0.55f;

        public override void SafeSetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.idStaticNPCHitCooldown = 4;
            Projectile.ArmorPenetration = 75;
            Projectile.manualDirectionChange = true;
        }

        public override void OnSpawn(IEntitySource source)
        {
            Player player = Main.player[Projectile.owner];
            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            if (modPlayer.LastSide == TwoPathsSide.Dark)
                flipTimer = FlipTime;
            if (player.HeldItem.ModItem is TwoPaths item)
                shouldSpin = item.spin >= 3;
            if (player.whoAmI == Main.myPlayer)
                attackId = modPlayer.BeginAttack(TwoPathsSide.Light);
        }

        // Share of the swing covered at t (0 when the wind-up ends, 1 when the projectile ends):
        // a short ease-in, a burst, a hard stop, then the blade holds still.
        private float SwingShare(float t)
        {
            t = MathHelper.Clamp(t, 0f, 1f);
            if (t < 0.2f)
                return 0.06f * (t / 0.2f) * (t / 0.2f);
            if (t < BurstEnd)
                return 0.06f + 0.88f * MathF.Pow((t - 0.2f) / (BurstEnd - 0.2f), shouldSpin ? 1.1f : 1.4f);
            if (t < BurstEnd + StopTime)
                return 0.94f + 0.06f * MathF.Cbrt((t - BurstEnd) / StopTime);
            return 1f;
        }

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            if (Projectile.ai[1] <= 0f || player.dead || !player.active)
            {
                Projectile.Kill();
                return false;
            }

            Projectile.scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;
            Projectile.timeLeft = (int)Projectile.ai[1] + 1;
            player.itemTime = player.itemAnimation = (int)Projectile.ai[1];
            player.heldProj = Projectile.whoAmI;
            if (Projectile.velocity.X != 0f)
                player.ChangeDir(Projectile.velocity.X > 0f ? 1 : -1);

            float side = (Projectile.direction == -1 ? 1f : -1f) * player.direction;
            float bladeRotation;
            float armRotation;
            if (Progress > 0.75f)
            {
                if (player.whoAmI == Main.myPlayer)
                {
                    Projectile.velocity = (Main.MouseWorld - player.MountedCenter).SafeNormalize(Vector2.UnitX * player.direction);
                    Projectile.netUpdate = true;
                }
                float aim = Projectile.velocity.ToRotation();
                float windup = (Progress - 0.75f) * 4f;
                bladeRotation = aim + MathHelper.ToRadians(MathHelper.SmoothStep(135f, 80f, windup)) * side;
                armRotation = aim + MathHelper.Lerp(MathHelper.PiOver2, MathHelper.PiOver4, windup) * side;
                // The trail only remembers the swing itself, not the wind-up pulling the other way.
                trailCount = 0;
            }
            else
            {
                float aim = Projectile.velocity.ToRotation();
                float share = SwingShare(1f - Progress / 0.75f);
                float swing = 1f - share;
                float bladeEnd = MathHelper.ToRadians(shouldSpin ? 495f : 135f);
                float armEnd = shouldSpin ? MathHelper.TwoPi + MathHelper.PiOver2 : MathHelper.PiOver2;
                bladeRotation = aim + MathHelper.Lerp(MathHelper.ToRadians(-100f), bladeEnd, swing) * side;
                armRotation = aim + MathHelper.Lerp(-MathHelper.PiOver2, armEnd, swing) * side;
            }

            if (flipTimer > 0)
            {
                bladeRotation += MathHelper.Pi * (flipTimer / (float)FlipTime) * side;
                flipTimer--;
            }

            if (Projectile.localAI[2] == 0f && Projectile.ai[1] <= Projectile.ai[0] * 0.5f)
            {
                Projectile.localAI[2] = 1f;
                SoundEngine.PlaySound(SoundID.DD2_MonkStaffSwing with { Pitch = shouldSpin ? -0.2f : 0.1f }, player.Center);
                SoundEngine.PlaySound(SoundID.Item71 with { Pitch = 0.4f, Volume = 0.6f }, player.Center);
                if (player.whoAmI == Main.myPlayer)
                    SpawnJudgment(player);
            }

            mirror = side > 0f;
            for (int i = TrailLength - 1; i > 0; i--)
                trail[i] = trail[i - 1];
            trail[0] = bladeRotation;
            trailCount = Math.Min(trailCount + 1, TrailLength);

            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRotation - MathHelper.PiOver2);
            Projectile.Center = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRotation - MathHelper.PiOver2);
            Projectile.rotation = bladeRotation;
            Projectile.ai[1]--;
            return false;
        }

        private void SpawnJudgment(Player player)
        {
            Vector2 aim = (Main.MouseWorld - player.MountedCenter).SafeNormalize(Vector2.UnitX * player.direction);
            float[] angles = shouldSpin
                ? new[] { MathHelper.PiOver4, -MathHelper.PiOver4 }
                : new[] { MathHelper.PiOver4 * Projectile.direction + Main.rand.NextFloat(-0.15f, 0.15f) };

            foreach (float angle in angles)
            {
                Vector2 direction = aim.RotatedBy(angle);
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Main.MouseWorld - direction * SlashDistance, direction * 16f, ModContent.ProjectileType<TwoPathsJudgmentSlash>(), Projectile.damage, Projectile.knockBack, player.whoAmI, attackId);
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Main.MouseWorld, direction, ModContent.ProjectileType<TwoPathsRift>(), Projectile.damage, Projectile.knockBack, player.whoAmI, attackId, TwoPathsRift.LightStyle, RiftLength);
            }
        }

        public override bool? CanDamage() => Progress < 0.6f && Progress > 0.1f ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float collisionPoint = 0f;
            Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * TwoPathsHelper.BladeReach * Projectile.scale;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, tip, 60f * Projectile.scale, ref collisionPoint);
        }

        public override void SafeOnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Thorium maps soul essence through the item's shoot type; this projectile is registered in TwoPathsSystem, this is the fallback.
            if (ScytheCharge == 0 && CanGiveScytheCharge)
            {
                Player player = Main.player[Projectile.owner];
                CanGiveScytheCharge = false;
                player.AddBuff(ModContent.BuffType<SoulEssence>(), 1800);
                CombatText.NewText(player.Hitbox, new Color(100, 255, 200), SoulCharge, false, true);
                player.GetThoriumPlayer().soulEssence += SoulCharge;
            }
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Light, attackId);
            TwoPathsHelper.PlayImpactSound(Projectile, TwoPathsHelper.LightImpactSound, target.Center);
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(shouldSpin);
            writer.Write(Projectile.direction);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            shouldSpin = reader.ReadBoolean();
            Projectile.direction = reader.ReadInt32();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (Progress < 0.75f)
            {
                // Only during the swing (the wind-up moves the blade too); after the hard stop the trail shrinks back and fades.
                float t = 1f - Progress / 0.75f;
                float smear = MathHelper.Clamp(1f - (t - BurstEnd - StopTime) / SmearLinger, 0f, 1f);
                if (smear > 0.02f)
                {
                    TwoPathsHelper.DrawSwingTrail(Projectile.Center, trail, trailCount, TwoPathsHelper.BladeReach * Projectile.scale, TrailAngle, smear, TwoPathsHelper.LightTrail);
                    for (int i = Math.Min(trailCount, GhostCount) - 1; i >= 1; i--)
                        TwoPathsHelper.DrawScythe(Projectile.Center, trail[i], TwoPathsSide.Light, mirror, Projectile.scale, TwoPathsHelper.Additive(TwoPathsHelper.LightGold, 0.4f * smear * (1f - i / (float)GhostCount)));
                }
            }

            TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Light, mirror, Projectile.scale, Color.Lerp(lightColor, Color.White, 0.5f));
            return false;
        }
    }
}
