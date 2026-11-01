using System;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod.Projectiles.Scythe;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // Left click: heavy three-stage swings (forehand, backhand, a big spin that tears a rift through every enemy nearby).
    // Every swing flings out blood skulls that hunt nearby enemies. Each stage starts where the previous one stopped:
    // the blade holds still while gathering, then sweeps in a burst and stops hard. The scythe itself is drawn at the shared
    // size; a big afterimage of its blade head swings out with the burst and gives the swing its reach.
    // ai[0] = attack time, ai[1] = countdown, ai[2] = stage (0..2). The aim is locked when the swing starts.
    public class TwoPathsDarkHeld : ScythePro
    {
        private const float PhantomFactor = 2f;
        private const float PhantomSwellAngle = MathHelper.Pi / 3f;
        private const float HitWidth = 80f;
        private const float RiftRadius = 640f;
        private const float SkullDamage = 0.35f;
        private const int TrailLength = 9;
        private const float TrailAngle = 2.6f;
        private const float SmearLinger = 0.12f;
        private const int FlipTime = 6;
        private const int SpinStage = 2;

        // Per stage: blade angles in degrees relative to the aim for a player facing right (mirrored when facing left),
        // the damaging window (share of the swing) and when skulls are released. Stages chain end to start.
        private static readonly float[] StartAngle = { -150f, 140f, -150f };
        private static readonly float[] EndAngle = { 140f, -150f, 570f };
        private static readonly float[] FastStart = { 0.5f, 0.5f, 0.35f };
        private static readonly float[] FastEnd = { 0.64f, 0.64f, 0.71f };
        private static readonly float[][] SkullTimes =
        {
            new[] { 0.53f, 0.56f, 0.6f },
            new[] { 0.53f, 0.56f, 0.6f },
            new[] { 0.4f, 0.45f, 0.5f, 0.55f, 0.6f, 0.65f },
        };

        private static readonly Color DarkBody = new(70, 0, 14);

        private bool mirror;
        private int attackId;
        private int flipTimer;
        private bool swingStarted;
        private bool midSpinSound;
        private bool riftSpawned;
        private int nextSkull;
        private float burstStart;
        private float phantomSwell;
        private readonly float[] trail = new float[TrailLength];
        private int trailCount;

        public override string Texture => TwoPathsHelper.TexturePath;

        private int Stage => (int)Projectile.ai[2];
        private float Progress => 1f - Projectile.ai[1] / Projectile.ai[0];
        private bool InFastPhase => Progress > FastStart[Stage] && Progress < FastEnd[Stage];
        private Vector2 AimDirection => Projectile.velocity.SafeNormalize(Vector2.UnitX);
        private float PhantomScale => Projectile.scale * PhantomFactor;

        public override void SafeSetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.usesIDStaticNPCImmunity = false;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 4;
            Projectile.ArmorPenetration = 75;
            Projectile.manualDirectionChange = true;
        }

        public override void OnSpawn(IEntitySource source)
        {
            Player player = Main.player[Projectile.owner];
            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            if (modPlayer.LastSide == TwoPathsSide.Light)
                flipTimer = FlipTime;
            if (player.whoAmI == Main.myPlayer)
                attackId = modPlayer.BeginAttack(TwoPathsSide.Dark);
        }

        // Share of the arc covered at progress x: half the swing gathering with a slight pull-back,
        // a tenth bursting through the arc, a hard stop, then a short drift.
        private static float SwingCurve(float x)
        {
            if (x < 0.5f)
                return -0.06f * MathF.Sqrt(x / 0.5f);
            if (x < 0.6f)
                return -0.06f + 0.98f * MathF.Pow((x - 0.5f) / 0.1f, 1.6f);
            if (x < 0.633f)
                return 0.92f + 0.08f * MathF.Cbrt((x - 0.6f) / 0.033f);
            return 1f + 0.04f * ((x - 0.633f) / 0.367f);
        }

        // Spin: gather, ease in, two turns at a steady high speed, hard stop.
        private static float SpinCurve(float x)
        {
            if (x < 0.35f)
                return -0.04f * MathF.Sqrt(x / 0.35f);
            if (x < 0.42f)
            {
                float s = (x - 0.35f) / 0.07f;
                return -0.04f + 0.14f * s * s;
            }
            if (x < 0.68f)
                return 0.1f + 0.86f * ((x - 0.42f) / 0.26f);
            if (x < 0.71f)
                return 0.96f + 0.04f * MathF.Cbrt((x - 0.68f) / 0.03f);
            return 1f + 0.01f * ((x - 0.71f) / 0.29f);
        }

        private float Curve(float x) => Stage == SpinStage ? SpinCurve(x) : SwingCurve(x);

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            if (Projectile.ai[1] <= 0f || player.dead || !player.active)
            {
                Projectile.Kill();
                return false;
            }

            Projectile.timeLeft = (int)Projectile.ai[1] + 1;
            player.itemTime = player.itemAnimation = (int)Projectile.ai[1];
            player.heldProj = Projectile.whoAmI;
            int facing = Projectile.velocity.X >= 0f ? 1 : -1;
            player.ChangeDir(facing);

            float aim = Projectile.velocity.ToRotation();
            float start = aim + MathHelper.ToRadians(StartAngle[Stage]) * facing;
            float end = aim + MathHelper.ToRadians(EndAngle[Stage]) * facing;
            float x = MathHelper.Clamp(Progress, 0f, 1f);
            float bladeRotation = MathHelper.Lerp(start, end, Curve(x));
            mirror = end < start;
            float sweep = end > start ? 1f : -1f;

            bladeRotation += MathHelper.Pi * (flipTimer / (float)FlipTime) * facing;
            if (flipTimer > 0)
                flipTimer--;

            // The afterimage stays hidden through the gathering and swells out over the first PhantomSwellAngle of the burst.
            if (x < FastStart[Stage])
                burstStart = bladeRotation;
            phantomSwell = MathHelper.Clamp(MathF.Abs(bladeRotation - burstStart) / PhantomSwellAngle, 0f, 1f);

            if (!swingStarted && x >= FastStart[Stage] - 0.03f)
            {
                swingStarted = true;
                PlaySwingSound(player);
                if (player.whoAmI == Main.myPlayer)
                    Punch(player, (aim + MathHelper.PiOver2 * sweep).ToRotationVector2(), 4f);
            }
            if (Stage == SpinStage && !midSpinSound && x >= 0.55f)
            {
                midSpinSound = true;
                PlaySwingSound(player);
            }

            float[] skullTimes = SkullTimes[Stage];
            while (nextSkull < skullTimes.Length && x >= skullTimes[nextSkull])
            {
                // One ghostly whoosh per swing, as the first skull breaks out.
                if (nextSkull == 0)
                    SoundEngine.PlaySound(SoundID.DD2_GhastlyGlaivePierce with { Pitch = -0.3f, Volume = 0.55f, PitchVariance = 0.15f }, player.Center);
                nextSkull++;
                if (player.whoAmI == Main.myPlayer)
                    ReleaseSkull(player, bladeRotation);
            }

            if (Stage == SpinStage && !riftSpawned && x >= FastEnd[Stage])
            {
                riftSpawned = true;
                TearRifts(player);
            }

            if (InFastPhase)
                SpawnSparks(player, bladeRotation, sweep);

            // The trail only remembers the burst onward, not the gathering pull-back or the side flip.
            if (x < FastStart[Stage])
                trailCount = 0;
            for (int i = TrailLength - 1; i > 0; i--)
                trail[i] = trail[i - 1];
            trail[0] = bladeRotation;
            trailCount = Math.Min(trailCount + 1, TrailLength);

            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, bladeRotation - MathHelper.PiOver2);
            Projectile.Center = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, bladeRotation - MathHelper.PiOver2);
            Projectile.rotation = bladeRotation;
            Projectile.scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;
            Projectile.ai[1]--;
            return false;
        }

        private void PlaySwingSound(Player player)
        {
            SoundEngine.PlaySound(SoundID.Item71 with { Pitch = Stage switch { 0 => -0.4f, 1 => -0.2f, _ => -0.05f }, PitchVariance = 0.1f }, player.Center);
            SoundEngine.PlaySound(SoundID.DD2_MonkStaffSwing with { Pitch = -0.6f, Volume = 0.8f }, player.Center);
        }

        // Skulls burst out of the player's chest in the direction the blade is pointing, so they fan out with the swing.
        private void ReleaseSkull(Player player, float rotation)
        {
            Vector2 chest = player.MountedCenter + new Vector2(0f, -4f * player.gravDir);
            Vector2 velocity = rotation.ToRotationVector2().RotatedByRandom(0.2f) * 11f;
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), chest, velocity, ModContent.ProjectileType<TwoPathsSkull>(), (int)(Projectile.damage * SkullDamage), Projectile.knockBack * 0.5f, player.whoAmI, attackId);
        }

        // Finisher: a dark rift opens across every enemy in range, and a ring of dark energy shows the range.
        private void TearRifts(Player player)
        {
            SoundEngine.PlaySound(SoundID.DD2_BetsyFireballImpact with { Pitch = -0.6f, Volume = 0.9f }, player.Center);
            for (int i = 0; i < 48; i++)
            {
                Vector2 direction = (MathHelper.TwoPi * i / 48f).ToRotationVector2();
                Dust dust = Dust.NewDustPerfect(player.Center + direction * 40f, i % 2 == 0 ? DustID.Shadowflame : DustID.CrimsonTorch, direction * RiftRadius / 30f);
                dust.noGravity = true;
                dust.scale = 1.6f;
            }

            if (player.whoAmI != Main.myPlayer)
                return;
            Punch(player, AimDirection, 5f);
            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (!npc.CanBeChasedBy() || npc.DistanceSQ(player.Center) > RiftRadius * RiftRadius || !Collision.CanHitLine(player.Center, 1, 1, npc.Center, 1, 1))
                    continue;
                Vector2 direction = Main.rand.NextVector2Unit();
                float length = MathHelper.Max(npc.width, npc.height) * 1.6f + 120f;
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), npc.Center, direction, ModContent.ProjectileType<TwoPathsRift>(), Projectile.damage, Projectile.knockBack, player.whoAmI, attackId, TwoPathsRift.DarkStyle, length);
            }
        }

        private static void Punch(Player player, Vector2 direction, float strength)
        {
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(player.Center, direction, strength, 10f, 14, 1000f, "TwoPathsDark"));
        }

        private void SpawnSparks(Player player, float rotation, float sweep)
        {
            Vector2 tangent = (rotation + MathHelper.PiOver2 * sweep).ToRotationVector2();
            for (int i = 0; i < 5; i++)
            {
                Vector2 position = Projectile.Center + rotation.ToRotationVector2() * Main.rand.NextFloat(0.55f, 1f) * TwoPathsHelper.BladeReach * PhantomScale;
                Dust dust = Dust.NewDustPerfect(position, Main.rand.NextBool() ? DustID.CrimsonTorch : DustID.Shadowflame, tangent * Main.rand.NextFloat(3f, 7f) + player.velocity);
                dust.noGravity = true;
                dust.scale = Main.rand.NextFloat(1.1f, 1.7f);
            }
        }

        public override bool? CanDamage() => InFastPhase ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // The hitbox follows the big afterimage and is fatter than its blade on purpose, so the heavy swing feels generous.
            float collisionPoint = 0f;
            Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * TwoPathsHelper.BladeReach * PhantomScale;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, tip, HitWidth * PhantomScale, ref collisionPoint);
        }

        public override void SafeOnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Dark, attackId);
            Player player = Main.player[Projectile.owner];
            if (player.whoAmI == Main.myPlayer)
            {
                TwoPathsHelper.PlayImpactSound(Projectile, SoundID.DD2_BetsyFireballImpact with { Volume = 0.4f, PitchVariance = 0.2f }, target.Center);
                Punch(player, (target.Center - player.Center).SafeNormalize(AimDirection), 2f);
            }
            for (int i = 0; i < 6; i++)
            {
                Dust dust = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(target.width * 0.4f, target.height * 0.4f), Main.rand.NextBool() ? DustID.CrimsonTorch : DustID.Shadowflame, Main.rand.NextVector2Circular(6f, 6f));
                dust.noGravity = true;
                dust.scale = 1.4f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Shown from the start of the burst; after the hard stop the trail shrinks back into the blade and fades out.
            float smear = Progress < FastStart[Stage] ? 0f : MathHelper.Clamp(1f - (Progress - FastEnd[Stage]) / SmearLinger, 0f, 1f);
            if (smear > 0.02f)
            {
                // The big afterimage grows out of the blade head and swings along with it, with the trail at its size.
                float phantomScale = MathHelper.Lerp(Projectile.scale, PhantomScale, MathF.Sqrt(phantomSwell));
                float phantomOpacity = smear * phantomSwell;
                TwoPathsHelper.DrawSwingTrail(Projectile.Center, trail, trailCount, TwoPathsHelper.BladeReach * phantomScale, TrailAngle, smear, TwoPathsHelper.DarkTrail);
                Color ghost = Color.Lerp(DarkBody, TwoPathsHelper.DarkCrimson, 0.35f);
                TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Dark, mirror, phantomScale, ghost * (0.45f * phantomOpacity), TwoPathsHelper.PhantomDarkPath);
                TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Dark, mirror, phantomScale, TwoPathsHelper.Additive(TwoPathsHelper.DarkCrimson, 0.4f * phantomOpacity), TwoPathsHelper.PhantomDarkPath);
            }

            TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Dark, mirror, Projectile.scale, Color.Lerp(lightColor, Color.White, 0.4f));
            return false;
        }
    }
}
