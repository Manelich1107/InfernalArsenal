using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // Blood skull released by the left-click swings: bursts out of the player's chest, then hunts nearby enemies.
    // Uses Calamity's Gael's Greatsword skull sprite at a smaller scale. ai[0] = attack id.
    public class TwoPathsSkull : ModProjectile
    {
        private const int LaunchTicks = 14;
        private const int FadeTicks = 30;
        private const float SeekRange = 720f;
        private const float SeekSpeed = 17f;

        private ref float Age => ref Projectile.localAI[0];
        private float Opacity => Projectile.timeLeft < FadeTicks ? Projectile.timeLeft / (float)FadeTicks : 1f;

        public override string Texture => "CalamityMod/Projectiles/Melee/GaelSkull";

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 5;
            ProjectileID.Sets.TrailCacheLength[Type] = 6;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.scale = 0.6f;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            Projectile.penetrate = 1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 200;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            Age++;
            if (Age == 1)
            {
                for (int i = 0; i < 8; i++)
                {
                    Dust dust = Dust.NewDustPerfect(Projectile.Center, i % 2 == 0 ? DustID.Blood : DustID.CrimsonTorch, Projectile.velocity.RotatedByRandom(0.6f) * Main.rand.NextFloat(0.15f, 0.45f), 0, default, 1.3f);
                    dust.noGravity = i % 2 == 1;
                }
            }
            if (Age <= LaunchTicks)
                Projectile.velocity *= 0.9f;
            else
            {
                NPC target = FindTarget();
                if (target != null)
                {
                    Vector2 desired = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX) * SeekSpeed;
                    Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.12f);
                }
                else if (Projectile.timeLeft > FadeTicks)
                    Projectile.timeLeft = FadeTicks;
            }

            Projectile.spriteDirection = Projectile.velocity.X >= 0f ? 1 : -1;
            Projectile.rotation = Projectile.spriteDirection > 0 ? Projectile.velocity.ToRotation() : (-Projectile.velocity).ToRotation();
            if (++Projectile.frameCounter % 5 == 0)
                Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];

            if (Main.rand.NextBool(3))
            {
                Dust dust = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(6f, 6f), DustID.Rain_BloodMoon, -Projectile.velocity * 0.15f, 0, new Color(188, 126, 154), 1.1f * Opacity);
                dust.noGravity = true;
            }
        }

        private NPC FindTarget()
        {
            NPC best = null;
            float bestDistance = SeekRange * SeekRange;
            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (!npc.CanBeChasedBy())
                    continue;
                float distance = npc.DistanceSQ(Projectile.Center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = npc;
                }
            }
            return best;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Dark, (int)Projectile.ai[0]);
            target.AddBuff(BuffID.ShadowFlame, 120);
        }

        public override void OnKill(int timeLeft)
        {
            for (int i = 0; i < 8; i++)
            {
                Dust dust = Dust.NewDustPerfect(Projectile.Center, DustID.Rain_BloodMoon, Main.rand.NextVector2Circular(3f, 3f), 0, new Color(188, 126, 154), 1.2f);
                dust.noGravity = true;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Rectangle frame = texture.Frame(1, Main.projFrames[Type], 0, Projectile.frame);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Color color = Color.Lerp(lightColor, Color.White, 0.5f) * Opacity;

            for (int i = Projectile.oldPos.Length - 1; i >= 1; i--)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;
                float fade = 1f - i / (float)Projectile.oldPos.Length;
                Main.EntitySpriteDraw(texture, Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition, frame, color * (0.5f * fade), Projectile.rotation, origin, Projectile.scale, effects, 0f);
            }
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, color, Projectile.rotation, origin, Projectile.scale, effects, 0f);
            return false;
        }
    }
}
