using System;
using System.Collections.Generic;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // One colour stop across a swing trail's width: radius as a share of the reach, premultiplied colour
    // (alpha-blended stops as usual, additive stops with A = 0, see TwoPathsHelper.Additive).
    public readonly record struct TrailStop(float Radius, Color Color);

    public static class TwoPathsHelper
    {
        public const string TexturePath = "InfernalEclipseWeaponsDLC/Content/Items/Weapons/Healer/Melee/Scythes/TwoPaths";
        public const string LightHalfPath = "InfernalEclipseWeaponsDLC/Content/Projectiles/HealerPro/Scythes/TwoPathsHalfLight";
        public const string DarkHalfPath = "InfernalEclipseWeaponsDLC/Content/Projectiles/HealerPro/Scythes/TwoPathsHalfDark";
        // The dark blade head and a short piece of shaft fading toward the hand, on the dark half's canvas.
        public const string PhantomDarkPath = "InfernalEclipseWeaponsDLC/Content/Projectiles/HealerPro/Scythes/TwoPathsPhantomDark";

        // Blade necks measured on TwoPaths.png (208x190). Each half keeps the whole shaft and ends 7 px before
        // the removed blade's neck with a pommel (tools/split_halves.py); the hand holds it near that end.
        private static readonly Vector2 LightNeck = new(62f, 48f);
        private static readonly Vector2 DarkNeck = new(150f, 133f);
        private const float HandleEndBeforeNeck = 7f;
        private const float GripFromHandleEnd = 10f;

        public const float BladeReach = 150f;
        // Every attack and the idle hold draw the scythe at this size (times the item's melee scale), so switching between
        // them never resizes it.
        public const float SwingScale = 1.2f;
        private const float TrailMaxStep = 0.09f;
        private const uint ImpactSoundGap = 12;

        private static uint lastImpactSound;

        private static BasicEffect trailEffect;

        private static readonly Vector2 Middle = (LightNeck + DarkNeck) / 2f;
        private static readonly Vector2 LightDirection = Vector2.Normalize(LightNeck - DarkNeck);
        private static readonly float GripFromMiddle = Vector2.Distance(LightNeck, DarkNeck) / 2f - HandleEndBeforeNeck - GripFromHandleEnd;
        private static readonly Vector2 LightGrip = Middle - LightDirection * GripFromMiddle;
        private static readonly Vector2 DarkGrip = Middle + LightDirection * GripFromMiddle;
        private static readonly float LightAxis = LightDirection.ToRotation();
        private static readonly float DarkAxis = (-LightDirection).ToRotation();

        // Distance from the middle of the shaft to either blade tip of the whole scythe, at scale 1.
        public static readonly float FullReach = BladeReach - GripFromMiddle;

        public static readonly Color LightGold = new(255, 210, 110);
        public static readonly Color DarkCrimson = new(200, 20, 40);
        public static readonly Color DarkViolet = new(178, 102, 255);
        private static readonly Color DarkTrailTint = new(150, 0, 26);
        // A bright crystal ring for light hits; the dark side's hits use a fiery impact.
        public static readonly SoundStyle LightImpactSound = SoundID.DD2_CrystalCartImpact with { Pitch = 0.2f, Volume = 0.6f, PitchVariance = 0.15f };
        private static readonly Color DarkRim = new(170, 0, 30);
        private static readonly Color LightRim = new(225, 150, 40);

        // Swing trail layers for DrawSwingTrail.
        // Dark: an alpha-blended body (transparent near the hand, black-red in the middle, crimson toward the rim), a solid
        // rim line, then a red glow. The rim is drawn alpha-blended first so it stays red on bright backgrounds, where
        // additive red alone turns peach.
        public static readonly TrailStop[][] DarkTrail =
        {
            new TrailStop[]
            {
                new(0.25f, Color.Transparent),
                new(0.5f, Shaded(DarkTrailTint, 0.25f, 0.57f)),
                new(0.75f, Shaded(DarkTrailTint, 0.6f, 1f)),
                new(0.95f, Shaded(DarkTrailTint, 1f, 1f)),
                new(1f, Color.Transparent),
            },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, DarkRim), new(0.995f, Color.Transparent) },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, Additive(DarkCrimson, 0.7f)), new(0.995f, Color.Transparent) },
        };

        // Light: a translucent gold body, an additive glow that brightens toward the rim, a solid gold rim line and a
        // white-hot core. The alpha-blended layers keep it readable on bright backgrounds, where additive gold washes out.
        public static readonly TrailStop[][] LightTrail =
        {
            new TrailStop[]
            {
                new(0.25f, Color.Transparent),
                new(0.5f, Shaded(LightGold, 1f, 0.23f)),
                new(0.75f, Shaded(LightGold, 1f, 0.4f)),
                new(0.95f, Shaded(LightGold, 1f, 0.4f)),
                new(1f, Color.Transparent),
            },
            new TrailStop[]
            {
                new(0.25f, Color.Transparent),
                new(0.5f, Additive(LightGold, 0.08f)),
                new(0.75f, Additive(LightGold, 0.33f)),
                new(0.95f, Additive(LightGold, 0.55f)),
                new(1f, Color.Transparent),
            },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, LightRim), new(0.995f, Color.Transparent) },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, Additive(Color.White, 0.8f)), new(0.995f, Color.Transparent) },
        };

        public static Color Additive(Color color, float opacity = 1f)
        {
            color *= opacity;
            color.A = 0;
            return color;
        }

        // Premultiplied colour for an alpha-blended trail stop: the tint darkened by shade, at the given opacity.
        public static Color Shaded(Color tint, float shade, float alpha) => new(new Vector4(tint.ToVector3() * shade * alpha, alpha));

        // Draws a swing trail: a ring sector around center through the blade's recent angles (newest first). It fades to
        // nothing at most maxAngle behind the blade, and also with age, so it only covers ground the blade actually swept
        // and shrinks back into the blade once it stops. Each layer lays its colour stops across the radius, drawn in order.
        public static void DrawSwingTrail(Vector2 center, float[] angles, int count, float reach, float maxAngle, float opacity, params TrailStop[][] layers)
        {
            if (count < 2 || opacity <= 0f)
                return;

            // Fill in the gaps between ticks (the burst covers up to ~48 degrees per tick) so the arc stays round.
            List<float> sampleAngles = new();
            List<float> sampleFade = new();
            float travelled = 0f;
            bool capped = false;
            for (int i = 0; i < count - 1 && !capped; i++)
            {
                float span = angles[i + 1] - angles[i];
                float length = MathF.Abs(span);
                int steps = Math.Clamp((int)MathF.Ceiling(length / TrailMaxStep), 1, 16);
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    float distance = travelled + length * t;
                    if (distance >= maxAngle)
                        break;
                    sampleAngles.Add(angles[i] + span * t);
                    sampleFade.Add(MathF.Pow(1f - distance / maxAngle, 1.5f) * (1f - (i + t) / (count - 1)));
                }
                if (travelled + length >= maxAngle)
                {
                    sampleAngles.Add(angles[i] + MathF.Sign(span) * (maxAngle - travelled));
                    sampleFade.Add(0f);
                    capped = true;
                }
                travelled += length;
            }
            if (!capped)
            {
                sampleAngles.Add(angles[count - 1]);
                sampleFade.Add(0f);
            }

            GraphicsDevice device = Main.instance.GraphicsDevice;
            trailEffect ??= new BasicEffect(device) { VertexColorEnabled = true };
            trailEffect.World = Matrix.Identity;
            trailEffect.View = Main.GameViewMatrix.TransformationMatrix;
            trailEffect.Projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -1f, 1f);

            Main.spriteBatch.End();
            device.BlendState = BlendState.AlphaBlend;
            device.RasterizerState = RasterizerState.CullNone;
            device.DepthStencilState = DepthStencilState.None;
            trailEffect.CurrentTechnique.Passes[0].Apply();

            Vector2 origin = center - Main.screenPosition;
            int samples = sampleAngles.Count;
            foreach (TrailStop[] stops in layers)
            {
                int rings = stops.Length;
                var vertices = new VertexPositionColor[samples * rings];
                for (int k = 0; k < samples; k++)
                {
                    Vector2 direction = sampleAngles[k].ToRotationVector2();
                    for (int j = 0; j < rings; j++)
                        vertices[k * rings + j] = new VertexPositionColor(new Vector3(origin + direction * stops[j].Radius * reach, 0f), stops[j].Color * (sampleFade[k] * opacity));
                }

                var indices = new short[(samples - 1) * (rings - 1) * 6];
                int n = 0;
                for (int k = 0; k < samples - 1; k++)
                {
                    for (int j = 0; j < rings - 1; j++)
                    {
                        short a = (short)(k * rings + j);
                        short c = (short)(a + rings);
                        indices[n++] = a;
                        indices[n++] = (short)(a + 1);
                        indices[n++] = c;
                        indices[n++] = (short)(a + 1);
                        indices[n++] = (short)(c + 1);
                        indices[n++] = c;
                    }
                }
                device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, indices.Length / 3);
            }

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        }

        public static void Unload()
        {
            BasicEffect effect = trailEffect;
            trailEffect = null;
            if (effect != null)
                Main.QueueMainThreadAction(effect.Dispose);
        }

        // Only the attacking half is drawn while swinging. Mirror when the swing angle decreases so the curved edge leads.
        // texturePath swaps in another sprite laid out on that half's canvas.
        public static void DrawScythe(Vector2 gripWorld, float bladeRotation, TwoPathsSide activeEnd, bool mirror, float scale, Color color, string texturePath = null)
        {
            bool light = activeEnd == TwoPathsSide.Light;
            Texture2D texture = ModContent.Request<Texture2D>(texturePath ?? (light ? LightHalfPath : DarkHalfPath), AssetRequestMode.ImmediateLoad).Value;
            Vector2 origin = light ? LightGrip : DarkGrip;
            float axis = light ? LightAxis : DarkAxis;
            SpriteEffects effects = SpriteEffects.None;
            if (mirror)
            {
                origin.Y = texture.Height - origin.Y;
                axis = -axis;
                effects = SpriteEffects.FlipVertically;
            }

            Main.EntitySpriteDraw(texture, gripWorld - Main.screenPosition, null, color, bladeRotation - axis, origin, scale, effects, 0f);
        }

        // The whole double-headed scythe held by the middle of the shaft, with the light blade pointing along lightRotation.
        // The halves share TwoPaths.png's canvas, so passing one of them draws just that half in the same place.
        public static void DrawFullScythe(Vector2 middleWorld, float lightRotation, float scale, Color color, string texturePath = TexturePath)
        {
            Texture2D texture = ModContent.Request<Texture2D>(texturePath, AssetRequestMode.ImmediateLoad).Value;
            Main.EntitySpriteDraw(texture, middleWorld - Main.screenPosition, null, color, lightRotation - LightAxis, Middle, scale, SpriteEffects.None, 0f);
        }

        // Draw data for one frame of the item sheet (laid out like TwoPaths.png), held at a point gripTowardDark texture pixels
        // from the middle of the shaft toward the dark end, with the light blade pointing along lightRotation.
        // flip mirrors the sprite for a player facing left.
        public static DrawData HeldFrameDrawData(Texture2D texture, Rectangle frame, Vector2 screenPosition, float lightRotation, float gripTowardDark, float scale, Color color, bool flip)
        {
            Vector2 origin = Middle - LightDirection * gripTowardDark;
            float axis = LightAxis;
            SpriteEffects effects = SpriteEffects.None;
            if (flip)
            {
                origin.X = frame.Width - origin.X;
                axis = MathHelper.Pi - LightAxis;
                effects = SpriteEffects.FlipHorizontally;
            }
            return new DrawData(texture, screenPosition, frame, color, lightRotation - axis, origin, scale, effects, 0f);
        }

        public static void Credit(Projectile projectile, TwoPathsSide side, int attackId)
        {
            if (projectile.owner == Main.myPlayer)
                Main.player[projectile.owner].GetModPlayer<TwoPathsPlayer>().CreditHit(side, attackId);
        }

        // Hit sounds, played by the owner only and at most once every ImpactSoundGap ticks, so a swing striking several
        // enemies at once or the spin's repeated hits don't pile up into noise.
        public static void PlayImpactSound(Projectile projectile, in SoundStyle style, Vector2 position)
        {
            if (projectile.owner != Main.myPlayer || Main.GameUpdateCount - lastImpactSound < ImpactSoundGap)
                return;
            lastImpactSound = Main.GameUpdateCount;
            SoundEngine.PlaySound(style, position);
        }
    }
}
