using System;
using CalamityMod;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod.Projectiles.Scythe;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // Balance: the whole double-headed scythe comes out and spins while either button is held, draining both energies.
    // It merges the two sides: a red trail behind the dark blade and a gold one behind the light blade, judgment slashes
    // through the cursor that leave erosion marks, blood skulls from the chest, and every so often a gold-edged dark rift
    // crossed by a judgment slash (an X) on every enemy nearby. When the energy runs out it ends with a bigger X.
    // The first spin of a balance opens with the merge: the dark and light halves fly in from both sides, lock together at
    // the hand into the whole scythe, and it spins up straight into the spin.
    // ai[0] = spin direction (1 or -1), ai[1] = 1 for the merge intro; velocity holds the aim.
    public class TwoPathsBalanceSpin : ScythePro
    {
        private const float SpinSpeed = MathHelper.TwoPi / 18f;
        private const float HitReachFactor = 1.1f;
        private const float HitWidth = 60f;
        private const float SpinDamage = 0.35f;
        private const float SkullDamage = 0.35f;
        private const int SlashInterval = 30;
        private const int SkullInterval = 30;
        private const int CrossInterval = 90;
        private const float CrossRadius = 640f;
        private const float FinaleRadius = 960f;
        private const float FinaleRiftLength = 1400f;
        private const float FinaleSlashScale = 2.5f;
        private const int TrailLength = 9;
        private const float TrailAngle = 2.6f;
        private const int IntroFlight = 20;
        private const int IntroSpinUp = 12;
        private const int IntroMinSpin = 18;
        private static readonly Vector2 IntroStart = new(170f, -70f);

        private int age;
        private int attackId;
        private int baseDamage;
        private readonly float[] lightTrail = new float[TrailLength];
        private readonly float[] darkTrail = new float[TrailLength];
        private int trailCount;

        public override string Texture => TwoPathsHelper.TexturePath;

        private float Reach => TwoPathsHelper.FullReach * Projectile.scale;
        private bool Intro => Projectile.ai[1] == 1f;
        private int IntroEnd => Intro ? IntroFlight + IntroSpinUp : 0;
        private bool Merged => !Intro || age >= IntroFlight;

        public override void SafeSetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.usesIDStaticNPCImmunity = false;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;
            Projectile.ArmorPenetration = 75;
            Projectile.manualDirectionChange = true;
        }

        public override void OnSpawn(IEntitySource source)
        {
            Player player = Main.player[Projectile.owner];
            Projectile.rotation = Projectile.velocity.ToRotation();
            if (player.whoAmI != Main.myPlayer)
                return;
            // The blade itself hits often, so it does a share of the weapon damage; the slashes, skulls and rifts use the full value.
            baseDamage = Projectile.damage;
            Projectile.damage = (int)(baseDamage * SpinDamage);
            attackId = player.GetModPlayer<TwoPathsPlayer>().BeginBalanceAttack();
            SoundEngine.PlaySound((Intro ? SoundID.Item8 : SoundID.Item71) with { Pitch = -0.5f }, player.Center);
        }

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            if (player.dead || !player.active || !modPlayer.HoldingTwoPaths)
            {
                Projectile.Kill();
                return false;
            }

            if (player.whoAmI == Main.myPlayer)
            {
                // The intro always plays out and turns once more, even with no button held.
                bool held = player.controlUseItem || player.controlUseTile;
                if (!modPlayer.Balance || (!held && age >= IntroEnd + (Intro ? IntroMinSpin : 0)))
                {
                    Projectile.Kill();
                    return false;
                }

                if (age >= IntroEnd)
                {
                    modPlayer.DrainBalance(TwoPathsPlayer.BalanceDrainPerTick);
                    if (modPlayer.Dark <= 0f && modPlayer.Light <= 0f)
                    {
                        Finale(player);
                        Projectile.Kill();
                        return false;
                    }
                }

                // The aim stays put while the halves fly in, so they lock where they were heading.
                if (Merged)
                {
                    Vector2 aim = (Main.MouseWorld - player.MountedCenter).SafeNormalize(Vector2.UnitX * player.direction);
                    if (Vector2.Dot(aim, Projectile.velocity.SafeNormalize(aim)) < 0.98f)
                        Projectile.netUpdate = true;
                    Projectile.velocity = aim;
                }
                if (Intro && age == IntroFlight)
                    Lock(player, modPlayer);
                if (age >= IntroEnd)
                    Emit(player, age - IntroEnd);
            }

            Projectile.timeLeft = 2;
            player.itemTime = player.itemAnimation = 2;
            player.heldProj = Projectile.whoAmI;
            player.ChangeDir(Projectile.velocity.X >= 0f ? 1 : -1);

            // The hand holds the middle of the shaft out toward the cursor while the scythe spins around it.
            // After the merge it spins up from standstill to full speed.
            float speed = SpinSpeed;
            if (Intro && age < IntroEnd)
                speed = age < IntroFlight ? 0f : SpinSpeed * MathF.Pow((age - IntroFlight) / (float)IntroSpinUp, 2f);
            Projectile.rotation += speed * Projectile.ai[0];
            float armRotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRotation);
            Projectile.Center = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRotation);
            Projectile.scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;

            for (int i = TrailLength - 1; i > 0; i--)
            {
                lightTrail[i] = lightTrail[i - 1];
                darkTrail[i] = darkTrail[i - 1];
            }
            lightTrail[0] = Projectile.rotation;
            darkTrail[0] = Projectile.rotation + MathHelper.Pi;
            trailCount = Math.Min(trailCount + 1, TrailLength);

            if (Merged)
            {
                if ((age - IntroEnd) % 18 == 0)
                    SoundEngine.PlaySound(SoundID.DD2_MonkStaffSwing with { Pitch = -0.3f, Volume = 0.5f, MaxInstances = 2 }, player.Center);
                SpawnSparks();
            }
            else
            {
                // A trail of sparks behind each flying half.
                IntroPositions(out Vector2 darkPosition, out Vector2 lightPosition, out _);
                Dust dark = Dust.NewDustPerfect(darkPosition + Main.rand.NextVector2Circular(12f, 12f), DustID.Shadowflame, Vector2.Zero);
                dark.noGravity = true;
                dark.scale = 1.4f;
                Dust light = Dust.NewDustPerfect(lightPosition + Main.rand.NextVector2Circular(12f, 12f), DustID.GoldFlame, Vector2.Zero);
                light.noGravity = true;
                light.scale = 1.4f;
            }
            age++;
            return false;
        }

        // Where the flying halves are during the intro, and how far each still has to turn (the dark one turns the other way).
        // The light half comes from the side the light blade will point to (the facing side), the dark half from behind.
        private void IntroPositions(out Vector2 darkPosition, out Vector2 lightPosition, out float turn, float lag = 0f)
        {
            float progress = MathHelper.Clamp((age - lag) / IntroFlight, 0f, 1f);
            float eased = progress * progress * progress;
            Vector2 lightStart = IntroStart * new Vector2(Projectile.ai[0], 1f);
            Vector2 darkStart = lightStart * new Vector2(-1f, 1f);
            darkPosition = Projectile.Center + darkStart * (1f - eased);
            lightPosition = Projectile.Center + lightStart * (1f - eased);
            turn = (1f - eased) * MathHelper.TwoPi * 1.25f;
        }

        // The halves meet: a clink, the balance burst (X of gold and violet sparks, a shake) and a ring at the hand.
        private void Lock(Player player, TwoPathsPlayer modPlayer)
        {
            SoundEngine.PlaySound(SoundID.Item37 with { Pitch = -0.3f }, player.Center);
            modPlayer.BalanceEntryEffects();
            Punch(player, Projectile.velocity, 6f);
            for (int i = 0; i < 24; i++)
            {
                Vector2 direction = (MathHelper.TwoPi * i / 24f).ToRotationVector2();
                Dust dust = Dust.NewDustPerfect(Projectile.Center, i % 2 == 0 ? DustID.GoldFlame : DustID.Shadowflame, direction * 6f);
                dust.noGravity = true;
                dust.scale = 1.6f;
            }
        }

        // Light: a judgment slash through the cursor. Dark: two skulls from the chest. Together: an X on every enemy nearby.
        // tick counts from the end of the intro.
        private void Emit(Player player, int tick)
        {
            if (tick % SlashInterval == 0)
            {
                Vector2 direction = Projectile.velocity.RotatedBy((Main.rand.NextBool() ? 1f : -1f) * MathHelper.PiOver4 + Main.rand.NextFloat(-0.2f, 0.2f));
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Main.MouseWorld - direction * 480f, direction * 16f, ModContent.ProjectileType<TwoPathsJudgmentSlash>(), baseDamage, Projectile.knockBack, player.whoAmI, attackId, 0f, 1f);
            }

            if (tick % SkullInterval == SkullInterval / 2)
            {
                Vector2 chest = player.MountedCenter + new Vector2(0f, -4f * player.gravDir);
                for (int i = 0; i < 2; i++)
                {
                    Vector2 velocity = (Projectile.rotation + MathHelper.Pi * i).ToRotationVector2().RotatedByRandom(0.2f) * 11f;
                    Projectile.NewProjectile(Projectile.GetSource_FromThis(), chest, velocity, ModContent.ProjectileType<TwoPathsSkull>(), (int)(baseDamage * SkullDamage), Projectile.knockBack * 0.5f, player.whoAmI, attackId);
                }
            }

            if (tick % CrossInterval == CrossInterval / 2)
                CrossPulse(player, CrossRadius, 4f);
        }

        // Every enemy in range gets a gold-edged dark rift that a judgment slash crosses (an X); a ring of both colours shows the range.
        private void CrossPulse(Player player, float radius, float punch)
        {
            SoundEngine.PlaySound(SoundID.DD2_BetsyFireballImpact with { Pitch = -0.4f, Volume = 0.8f }, player.Center);
            for (int i = 0; i < 48; i++)
            {
                Vector2 direction = (MathHelper.TwoPi * i / 48f).ToRotationVector2();
                Dust dust = Dust.NewDustPerfect(player.Center + direction * 40f, i % 2 == 0 ? DustID.GoldFlame : DustID.Shadowflame, direction * radius / 30f);
                dust.noGravity = true;
                dust.scale = 1.6f;
            }
            Punch(player, Projectile.velocity, punch);

            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (!npc.CanBeChasedBy() || npc.DistanceSQ(player.Center) > radius * radius || !Collision.CanHitLine(player.Center, 1, 1, npc.Center, 1, 1))
                    continue;
                float length = MathHelper.Max(npc.width, npc.height) * 1.6f + 120f;
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), npc.Center, Main.rand.NextVector2Unit(), ModContent.ProjectileType<TwoPathsRift>(), baseDamage, Projectile.knockBack, player.whoAmI, attackId, TwoPathsRift.BalanceDarkStyle, length);
            }
        }

        // The energy is spent: an X on every enemy in a wider range, a giant X through the player, and a heavy shake.
        private void Finale(Player player)
        {
            CrossPulse(player, FinaleRadius, 10f);
            SoundEngine.PlaySound(SoundID.Item74 with { Pitch = -0.4f, Volume = 0.8f }, player.Center);
            SoundEngine.PlaySound(SoundID.Item29 with { Pitch = -0.6f, Volume = 0.9f }, player.Center);
            player.SetScreenshake(6f);

            Vector2 riftDirection = Projectile.velocity.RotatedBy(MathHelper.PiOver4);
            Vector2 slashDirection = Projectile.velocity.RotatedBy(-MathHelper.PiOver4);
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), player.Center, riftDirection, ModContent.ProjectileType<TwoPathsRift>(), baseDamage, Projectile.knockBack, player.whoAmI, attackId, TwoPathsRift.BalanceDarkStyle, FinaleRiftLength);
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), player.Center - slashDirection * 480f * FinaleSlashScale, slashDirection * 16f * FinaleSlashScale, ModContent.ProjectileType<TwoPathsJudgmentSlash>(), baseDamage, Projectile.knockBack, player.whoAmI, attackId, FinaleSlashScale);

            for (int line = 0; line < 2; line++)
            {
                Vector2 axis = line == 0 ? riftDirection : slashDirection;
                for (int i = 0; i < 64; i++)
                {
                    float along = MathHelper.Lerp(-320f, 320f, i / 63f);
                    Dust dust = Dust.NewDustPerfect(player.Center + axis * along, line == 0 ? DustID.Shadowflame : DustID.GoldFlame, axis.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-2f, 2f) + axis * along * 0.03f);
                    dust.noGravity = true;
                    dust.scale = Main.rand.NextFloat(1.4f, 2.2f);
                }
            }
        }

        private static void Punch(Player player, Vector2 direction, float strength)
        {
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(player.Center, direction, strength, 10f, 16, 1000f, "TwoPathsBalance"));
        }

        private void SpawnSparks()
        {
            for (int end = 0; end < 2; end++)
            {
                Vector2 direction = (Projectile.rotation + MathHelper.Pi * end).ToRotationVector2();
                Vector2 tangent = direction.RotatedBy(MathHelper.PiOver2 * Projectile.ai[0]);
                Dust dust = Dust.NewDustPerfect(Projectile.Center + direction * Main.rand.NextFloat(0.6f, 1f) * Reach, end == 0 ? DustID.GoldFlame : DustID.CrimsonTorch, tangent * Main.rand.NextFloat(3f, 6f));
                dust.noGravity = true;
                dust.scale = Main.rand.NextFloat(1.1f, 1.6f);
            }
        }

        public override bool? CanDamage() => Merged ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Both blades cut: the line runs from tip to tip through the hand.
            Vector2 tip = Projectile.rotation.ToRotationVector2() * Reach * HitReachFactor;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - tip, Projectile.Center + tip, HitWidth * Projectile.scale, ref collisionPoint);
        }

        public override void SafeOnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Light, attackId);
            TwoPathsHelper.PlayImpactSound(Projectile, SoundID.DD2_BetsyFireballImpact with { Volume = 0.3f, PitchVariance = 0.2f }, target.Center);
            for (int i = 0; i < 6; i++)
            {
                Dust dust = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(target.width * 0.4f, target.height * 0.4f), i % 2 == 0 ? DustID.GoldFlame : DustID.CrimsonTorch, Main.rand.NextVector2Circular(6f, 6f));
                dust.noGravity = true;
                dust.scale = 1.3f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color color = Color.Lerp(lightColor, Color.White, 0.5f);
            if (!Merged)
            {
                // The halves fly in turning, with fading afterimages; each is laid out on the whole scythe's canvas,
                // so at the hand they line up into the whole scythe.
                for (int ghost = 3; ghost >= 0; ghost--)
                {
                    IntroPositions(out Vector2 darkPosition, out Vector2 lightPosition, out float turn, ghost * 1.5f);
                    float shade = ghost == 0 ? 1f : 0.35f * (1f - ghost / 4f);
                    Color darkColor = ghost == 0 ? color : TwoPathsHelper.Additive(TwoPathsHelper.DarkCrimson, shade);
                    Color lightColorTint = ghost == 0 ? color : TwoPathsHelper.Additive(TwoPathsHelper.LightGold, shade);
                    TwoPathsHelper.DrawFullScythe(darkPosition, Projectile.rotation - turn * Projectile.ai[0], Projectile.scale, darkColor, TwoPathsHelper.DarkHalfPath);
                    TwoPathsHelper.DrawFullScythe(lightPosition, Projectile.rotation + turn * Projectile.ai[0], Projectile.scale, lightColorTint, TwoPathsHelper.LightHalfPath);
                }
                return false;
            }

            TwoPathsHelper.DrawSwingTrail(Projectile.Center, darkTrail, trailCount, Reach, TrailAngle, 1f, TwoPathsHelper.DarkTrail);
            TwoPathsHelper.DrawSwingTrail(Projectile.Center, lightTrail, trailCount, Reach, TrailAngle, 1f, TwoPathsHelper.LightTrail);
            TwoPathsHelper.DrawFullScythe(Projectile.Center, Projectile.rotation, Projectile.scale, color);
            return false;
        }
    }
}
