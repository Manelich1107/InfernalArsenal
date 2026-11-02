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
    // 【右键 · 光攻击】瞄准光标的定向大挥，一次右键出手 = 一个这个弹幕 = 一刀。
    // - 节奏是“倒计时曲线”：前 25% 时间是前摇，刀跟着光标瞄准；之后方向锁定，短暂起势、爆发、硬停、定住。
    // - 每刀交替从两侧挥（方向由物品 TwoPaths.swingDirection 决定，放在 Projectile.direction 里）。
    // - 挥到一半时，在光标处放一道审判斩（TwoPathsJudgmentSlash）穿过光标，同一条线上再放一道光裂隙（TwoPathsRift），
    //   光裂隙晚 0.5 秒才裂开，算延迟回响。
    // - 每三刀一次 X 字终结（shouldSpin）：刀多抡一整圈，两道审判斩和两道光裂隙在光标处交叉成 X。是否终结由物品的右键计数决定。
    // - 画面：只画光端那一半（TwoPathsHalfLight.png），尺寸是统一的 SwingScale，带白金拖影和几个渐隐的金色残影。
    // ai[0] = 这一刀的总时长（tick），ai[1] = 剩余时长（每 tick 减 1）。
    // 注意：这里的 Progress 是从 1 倒数到 0，和左键 TwoPathsDarkHeld（从 0 数到 1）方向相反。
    public class TwoPathsLightHeld : ScythePro
    {
        // 审判斩从光标往后退 480 像素处出发，飞过光标（和审判斩的视觉长度一致）。
        private const float SlashDistance = 480f;
        // 光裂隙的长度（像素）。
        private const float RiftLength = 768f;
        // 从左键切到右键时，先用 6 tick 把镰刀翻转半圈（暗刃在前换成光刃在前）。
        private const int FlipTime = 6;
        // 直接命中给的灵魂精华层数（备用逻辑用，见 SafeOnHitNPC）。
        private const int SoulCharge = 4;
        // 拖影记录最近 7 tick 的刀身角度；其中最近的几个位置画金色残影。
        private const int TrailLength = 7;
        private const int GhostCount = 4;
        // 拖影最长拖到刀身后方约 130°（2.3 弧度）处完全消失。
        private const float TrailAngle = 2.3f;
        // 硬停占挥砍阶段的 7%；之后拖影再用 25% 缩回刀身并淡出。
        private const float StopTime = 0.07f;
        private const float SmearLinger = 0.25f;

        // 这一刀是不是 X 字终结。
        private bool shouldSpin;
        // 往回挥时竖直镜像贴图，让弯刃的一侧始终在前。
        private bool mirror;
        private int attackId;
        private int flipTimer;
        // 最近几 tick 的刀身角度（新的在前），给拖影和残影用。
        private readonly float[] trail = new float[TrailLength];
        private int trailCount;

        // 弹幕本身的贴图（实际绘制在 PreDraw 里自己做）。
        public override string Texture => TwoPathsHelper.TexturePath;

        // 剩余时间比例：1 是刚出手，0 是结束。大于 0.75 是前摇，小于 0.75 是挥砍阶段。
        private float Progress => Projectile.ai[1] / Projectile.ai[0];
        // 爆发结束的位置（挥砍阶段的比例）。X 字终结要多抡一圈，爆发更长。
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
            // 上一刀是左键（暗）时，先翻转镰刀。
            if (modPlayer.LastSide == TwoPathsSide.Dark)
                flipTimer = FlipTime;
            // 物品的右键计数读到 3 就是 X 字终结（物品在生成这个弹幕之后才推进计数，见 TwoPaths.spin）。
            if (player.HeldItem.ModItem is TwoPaths item)
                shouldSpin = item.spin >= 3;
            // 领一个出手编号，这一刀和它的审判斩、光裂隙共用，同一次出手只充一次能量。
            if (player.whoAmI == Main.myPlayer)
                attackId = modPlayer.BeginAttack(TwoPathsSide.Light);
        }

        // 挥砍阶段的曲线：输入 t（0 是前摇刚结束，1 是这一刀结束），返回已经挥过的比例。
        // 0~0.2 缓慢起势到 6%；0.2~BurstEnd 爆发到 94%；之后 StopTime 内硬停补到 100%；然后刀定住不动。
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

        // 每 tick 的逻辑。返回 false 跳过基类和原版的 AI，全部自己处理。
        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            if (Projectile.ai[1] <= 0f || player.dead || !player.active)
            {
                Projectile.Kill();
                return false;
            }

            // 统一尺寸：物品的近战尺寸加成 × SwingScale。
            Projectile.scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;
            // 让弹幕和玩家的出手动画持续到这一刀结束，期间不能出下一刀；heldProj 让这个弹幕作为手持物和玩家一起绘制。
            Projectile.timeLeft = (int)Projectile.ai[1] + 1;
            player.itemTime = player.itemAnimation = (int)Projectile.ai[1];
            player.heldProj = Projectile.whoAmI;
            if (Projectile.velocity.X != 0f)
                player.ChangeDir(Projectile.velocity.X > 0f ? 1 : -1);

            // side 决定从哪一侧挥：交替的方向 × 角色朝向。
            float side = (Projectile.direction == -1 ? 1f : -1f) * player.direction;
            float bladeRotation;
            float armRotation;
            if (Progress > 0.75f)
            {
                // 前摇：刀跟着光标瞄准（只有本人客户端读得到光标，改了方向要同步给其他人）。
                // 刀在瞄准方向的一侧从 80° 平滑拉到 135°，手臂从 45° 拉到 90°，像把刀往后拉。
                if (player.whoAmI == Main.myPlayer)
                {
                    Projectile.velocity = (Main.MouseWorld - player.MountedCenter).SafeNormalize(Vector2.UnitX * player.direction);
                    Projectile.netUpdate = true;
                }
                float aim = Projectile.velocity.ToRotation();
                float windup = (Progress - 0.75f) * 4f;
                bladeRotation = aim + MathHelper.ToRadians(MathHelper.SmoothStep(135f, 80f, windup)) * side;
                armRotation = aim + MathHelper.Lerp(MathHelper.PiOver2, MathHelper.PiOver4, windup) * side;
                // 拖影只记挥砍本身，不记前摇时反方向的回拉。
                trailCount = 0;
            }
            else
            {
                // 挥砍：方向已锁定。刀从 135° 挥到另一侧的 -100°（X 字终结从 495° 开始，多抡一整圈）。
                float aim = Projectile.velocity.ToRotation();
                float share = SwingShare(1f - Progress / 0.75f);
                float swing = 1f - share;
                float bladeEnd = MathHelper.ToRadians(shouldSpin ? 495f : 135f);
                float armEnd = shouldSpin ? MathHelper.TwoPi + MathHelper.PiOver2 : MathHelper.PiOver2;
                bladeRotation = aim + MathHelper.Lerp(MathHelper.ToRadians(-100f), bladeEnd, swing) * side;
                armRotation = aim + MathHelper.Lerp(-MathHelper.PiOver2, armEnd, swing) * side;
            }

            // 翻转动作：开头几 tick 额外加上半圈，逐渐收回到 0。
            if (flipTimer > 0)
            {
                bladeRotation += MathHelper.Pi * (flipTimer / (float)FlipTime) * side;
                flipTimer--;
            }

            // 时间过半时（挥砍阶段里，刀正挥得最快的时候）播放挥砍音效并放出审判斩，只放一次。localAI[2] 当“已放过”的标记。
            if (Projectile.localAI[2] == 0f && Projectile.ai[1] <= Projectile.ai[0] * 0.5f)
            {
                Projectile.localAI[2] = 1f;
                SoundEngine.PlaySound(SoundID.DD2_MonkStaffSwing with { Pitch = shouldSpin ? -0.2f : 0.1f }, player.Center);
                SoundEngine.PlaySound(SoundID.Item71 with { Pitch = 0.4f, Volume = 0.6f }, player.Center);
                if (player.whoAmI == Main.myPlayer)
                    SpawnJudgment(player);
            }

            // 记录刀身角度给拖影。
            mirror = side > 0f;
            for (int i = TrailLength - 1; i > 0; i--)
                trail[i] = trail[i - 1];
            trail[0] = bladeRotation;
            trailCount = Math.Min(trailCount + 1, TrailLength);

            // 前臂按 armRotation 摆，弹幕中心放在前手的位置（握点），刀身角度单独算。
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRotation - MathHelper.PiOver2);
            Projectile.Center = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRotation - MathHelper.PiOver2);
            Projectile.rotation = bladeRotation;
            Projectile.ai[1]--;
            return false;
        }

        // 在光标处放审判斩和光裂隙。普通刀：一道，斜 45°（随挥砍方向换边，带一点随机）。X 字终结：±45° 两道交叉。
        // 审判斩从光标往后退 SlashDistance 处出发，飞过光标；光裂隙直接以光标为中心。
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

        // 只在挥砍阶段的中段造成伤害（剩余时间 10%~60%）。
        public override bool? CanDamage() => Progress < 0.6f && Progress > 0.1f ? null : false;

        // 判定：从手到刀尖的线段，粗 60 × 尺寸。
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float collisionPoint = 0f;
            Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * TwoPathsHelper.BladeReach * Projectile.scale;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, tip, 60f * Projectile.scale, ref collisionPoint);
        }

        public override void SafeOnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // 瑟银按物品的 Item.shoot 发灵魂精华，这个弹幕不是 Item.shoot，已经在 TwoPathsSystem 里登记过；
            // 万一登记没生效（基类没给），这里手动给一次。
            if (ScytheCharge == 0 && CanGiveScytheCharge)
            {
                Player player = Main.player[Projectile.owner];
                CanGiveScytheCharge = false;
                player.AddBuff(ModContent.BuffType<SoulEssence>(), 1800);
                CombatText.NewText(player.Hitbox, new Color(100, 255, 200), SoulCharge, false, true);
                player.GetThoriumPlayer().soulEssence += SoulCharge;
            }
            // 充光能量（同一刀只算一次），播放命中音（有限流）。
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Light, attackId);
            TwoPathsHelper.PlayImpactSound(Projectile, TwoPathsHelper.LightImpactSound, target.Center);
        }

        // 多人同步：是否 X 字终结、挥砍方向只有生成方知道，要额外发给其他客户端。
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

        // 绘制顺序：拖影 → 金色残影 → 镰刀本体。返回 false 不让原版再画一次弹幕贴图。
        public override bool PreDraw(ref Color lightColor)
        {
            if (Progress < 0.75f)
            {
                // 拖影只在挥砍阶段画（前摇时刀也在动，但不画）；硬停后在 SmearLinger 内缩回刀身并淡出。
                float t = 1f - Progress / 0.75f;
                float smear = MathHelper.Clamp(1f - (t - BurstEnd - StopTime) / SmearLinger, 0f, 1f);
                if (smear > 0.02f)
                {
                    TwoPathsHelper.DrawSwingTrail(Projectile.Center, trail, trailCount, TwoPathsHelper.BladeReach * Projectile.scale, TrailAngle, smear, TwoPathsHelper.LightTrail);
                    // 在刀身最近经过的几个位置画叠加混合的金色残影，越旧越淡。
                    for (int i = Math.Min(trailCount, GhostCount) - 1; i >= 1; i--)
                        TwoPathsHelper.DrawScythe(Projectile.Center, trail[i], TwoPathsSide.Light, mirror, Projectile.scale, TwoPathsHelper.Additive(TwoPathsHelper.LightGold, 0.4f * smear * (1f - i / (float)GhostCount)));
                }
            }

            // 镰刀本体：只画光端那一半，受环境光影响但提亮 50%。
            TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Light, mirror, Projectile.scale, Color.Lerp(lightColor, Color.White, 0.5f));
            return false;
        }
    }
}
