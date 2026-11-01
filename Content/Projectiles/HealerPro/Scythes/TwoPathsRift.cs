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
using ThoriumMod.Buffs;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // ai[0] = attack id, ai[1] = style, ai[2] = length; velocity holds the direction.
    // Styles: light rift, dark rift, and the balance rift: a dark rift with a gold edge that a judgment slash crosses.
    public class TwoPathsRift : ModProjectile
    {
        public const int LightStyle = 0;
        public const int DarkStyle = 1;
        public const int BalanceDarkStyle = 2;
        private const int OpenTime = 30;
        private const int CrossDelay = 8;
        private const float CrossSlashScale = 0.5f;

        private static readonly Color DarkBody = new(70, 0, 14);

        public override string Texture => "Terraria/Images/Extra_98";

        private int Style => (int)Projectile.ai[1];
        private bool IsDark => Style == DarkStyle || Style == BalanceDarkStyle;
        private int Delay => IsDark ? 4 : 30;
        private float Length => Projectile.ai[2];
        private ref float Age => ref Projectile.localAI[0];

        private float Openness => Age < Delay ? 0f : MathF.Sin(MathHelper.Pi * MathHelper.Clamp((Age - Delay) / (float)OpenTime, 0f, 1f));

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 120;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation();
            if (Age == Delay)
                // Rifts opened together (a finisher or an X pulse) sound once.
                SoundEngine.PlaySound((IsDark ? SoundID.Item103 : SoundID.Item45) with { Pitch = IsDark ? -0.3f : 0.3f, Volume = 0.7f, MaxInstances = 1, SoundLimitBehavior = SoundLimitBehavior.IgnoreNew }, Projectile.Center);

            // Balance: a short judgment slash crosses the rift as it opens, so the two form an X at the enemy.
            if (Style == BalanceDarkStyle && Age == Delay + CrossDelay && Projectile.owner == Main.myPlayer)
            {
                Vector2 cross = Projectile.velocity.SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.PiOver2 + Main.rand.NextFloat(-0.35f, 0.35f));
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center - cross * 480f * CrossSlashScale, cross * 16f * CrossSlashScale, ModContent.ProjectileType<TwoPathsJudgmentSlash>(), Projectile.damage, Projectile.knockBack, Projectile.owner, Projectile.ai[0], CrossSlashScale);
            }

            if (Openness > 0.2f && Main.rand.NextBool(2))
            {
                Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitX);
                Dust dust = Dust.NewDustPerfect(Projectile.Center + direction * Main.rand.NextFloat(-0.5f, 0.5f) * Length, IsDark ? DustID.Shadowflame : DustID.GoldFlame, direction.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-2f, 2f));
                dust.noGravity = true;
            }

            if (++Age >= Delay + OpenTime)
                Projectile.Kill();
        }

        public override bool? CanDamage() => Openness > 0.25f ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 half = Projectile.velocity.SafeNormalize(Vector2.UnitX) * Length * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - half, Projectile.Center + half, 30f * Openness, ref collisionPoint);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, IsDark ? TwoPathsSide.Dark : TwoPathsSide.Light, (int)Projectile.ai[0]);
            if (IsDark)
                target.AddBuff(ModContent.BuffType<LightCurse>(), 180);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float open = Openness;
            if (open <= 0f)
                return false;

            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;
            Vector2 position = Projectile.Center - Main.screenPosition;
            float rotation = Projectile.rotation + MathHelper.PiOver2;
            float length = Length / texture.Height;

            if (IsDark)
            {
                // Alpha-blended dark body and black core; additive colour is kept for the thin edge only.
                Color edge = Style == BalanceDarkStyle ? TwoPathsHelper.LightGold : TwoPathsHelper.DarkCrimson;
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(edge, open * 0.8f), rotation, origin, new Vector2(1.5f * open, length * 1.02f), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, DarkBody * open, rotation, origin, new Vector2(1.2f * open, length), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.DarkViolet, open * 0.35f), rotation, origin, new Vector2(0.8f * open, length * 0.95f), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, Color.Black * open, rotation, origin, new Vector2(0.5f * open, length * 0.9f), SpriteEffects.None, 0f);
            }
            else
            {
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.LightGold, open), rotation, origin, new Vector2(1.2f * open, length), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(Color.White, open * 0.9f), rotation, origin, new Vector2(0.6f * open, length * 0.95f), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(Color.White, open), rotation, origin, new Vector2(0.25f * open, length * 0.9f), SpriteEffects.None, 0f);
            }
            return false;
        }
    }
}
