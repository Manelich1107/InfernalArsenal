using System;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // Balance spin: erosion marks keep surfacing on an enemy struck by one of its judgment slashes.
    // Each mark is a short dark slash across the enemy at a new angle. ai[0] = attack id, ai[1] = target NPC index.
    public class TwoPathsErosion : ModProjectile
    {
        private const int Marks = 3;
        private const int MarkInterval = 20;
        private const int MarkTime = 14;

        private static readonly Color DeepViolet = new(90, 20, 160);

        public override string Texture => "Terraria/Images/Extra_98";

        private NPC Target => Main.npc[(int)Projectile.ai[1]];
        private ref float Age => ref Projectile.localAI[0];
        private ref float MarkAngle => ref Projectile.localAI[1];
        private float Envelope => Age % MarkInterval < MarkTime ? MathF.Sin(MathHelper.Pi * (Age % MarkInterval) / MarkTime) : 0f;
        private float MarkLength => MathHelper.Max(Target.width, Target.height) * 1.5f + 90f;

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Marks * MarkInterval;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = MarkInterval;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            if (!Target.active || Target.life <= 0)
            {
                Projectile.Kill();
                return;
            }

            Projectile.Center = Target.Center;
            if (Age % MarkInterval == 0f)
            {
                MarkAngle = Main.rand.NextFloat(MathHelper.TwoPi);
                SoundEngine.PlaySound(SoundID.Item103 with { Pitch = 0.2f, Volume = 0.3f, MaxInstances = 1, SoundLimitBehavior = SoundLimitBehavior.IgnoreNew }, Projectile.Center);
                Vector2 direction = MarkAngle.ToRotationVector2();
                for (int i = 0; i < 12; i++)
                {
                    Dust dust = Dust.NewDustPerfect(Projectile.Center + direction * Main.rand.NextFloat(-0.5f, 0.5f) * MarkLength, DustID.Shadowflame, direction.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-4f, 4f));
                    dust.noGravity = true;
                    dust.scale = 1.4f;
                }
            }
            Age++;
        }

        public override bool? CanHitNPC(NPC target) => target.whoAmI == (int)Projectile.ai[1] ? null : false;

        public override bool? CanDamage() => Envelope > 0.5f ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 half = MarkAngle.ToRotationVector2() * MarkLength * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - half, Projectile.Center + half, 24f, ref collisionPoint);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Light, (int)Projectile.ai[0]);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float envelope = Envelope;
            if (envelope <= 0f)
                return false;

            // A wide violet glow, a deep violet edge, then an alpha-blended black core.
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;
            Vector2 position = Projectile.Center - Main.screenPosition;
            float rotation = MarkAngle + MathHelper.PiOver2;
            float length = MarkLength / texture.Height;
            Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.DarkViolet, envelope), rotation, origin, new Vector2(2.4f * envelope, length * 1.05f), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(texture, position, null, DeepViolet * envelope, rotation, origin, new Vector2(1.4f * envelope, length), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(texture, position, null, Color.Black * envelope, rotation, origin, new Vector2(0.8f * envelope, length * 0.92f), SpriteEffects.None, 0f);
            return false;
        }
    }
}
