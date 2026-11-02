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
    // 【左键 · 暗攻击】三段大开大合的重挥，一次左键出手 = 一个这个弹幕 = 一刀。
    // - 三段循环：0 正手、1 反手、2 抡圆的 720° 大旋转。段数由物品 TwoPaths.darkStage 决定并通过 ai[2] 传进来。
    //   每一段从上一段停下的角度开始，所以连着按就是首尾相接的一套连招。
    // - 每一刀的节奏：前一半几乎不动地蓄力（只轻微回拉），然后很短的时间爆发扫过整段弧，硬停，再留一小段收尾。
    //   只有爆发阶段（FastStart~FastEnd）造成伤害。
    // - 每一刀从胸口放出冤魂（TwoPathsSkull），追踪附近的敌人。大旋转转完时，640 像素内视线可达的每个敌人身上各开一道暗裂隙。
    // - 画面：镰刀本体只画暗端那一半（TwoPathsHalfDark.png），尺寸是统一的 SwingScale；爆发时从刀头长出一把 2 倍大的
    //   半透明暗红虚影（TwoPathsPhantomDark.png）跟着挥，拖影、火花和判定都按虚影的大小算，攻击距离靠它提供。
    // - 瞄准方向在出手那一刻锁定（velocity 由 TwoPaths.Shoot 设定，之后不再改）。
    // ai[0] = 这一刀的总时长（tick），ai[1] = 剩余时长（每 tick 减 1），ai[2] = 段数（0~2）。
    // 继承瑟银的 ScythePro：直接命中给灵魂精华、治疗者伤害类型等由基类处理。
    public class TwoPathsDarkHeld : ScythePro
    {
        // 虚影相对镰刀本体的倍数。本体是 1.2 倍（SwingScale），虚影就是 2.4 倍。
        private const float PhantomFactor = 2f;
        // 爆发开始后，虚影在前 60° 里从刀头长到完整大小、淡入。
        private const float PhantomSwellAngle = MathHelper.Pi / 3f;
        // 判定线的粗细（会再乘虚影倍数）。判定线从手一直连到虚影刀尖，故意比画面上的刀身更粗，重挥打起来更宽容。
        private const float HitWidth = 80f;
        // 大旋转终结开暗裂隙的范围（像素）。
        private const float RiftRadius = 640f;
        // 冤魂伤害 = 这一刀伤害 × 0.35。
        private const float SkullDamage = 0.35f;
        // 拖影记录最近 9 tick 的刀身角度。
        private const int TrailLength = 9;
        // 拖影最长拖到刀身后方约 150°（2.6 弧度）处完全消失。
        private const float TrailAngle = 2.6f;
        // 硬停之后，拖影和虚影还要再用这一刀时长的 12% 缩回刀身并淡出。
        private const float SmearLinger = 0.12f;
        // 从右键切到左键时，先用 6 tick 把镰刀翻转半圈（光刃在前换成暗刃在前）。
        private const int FlipTime = 6;
        private const int SpinStage = 2;

        // 每一段的参数（角色朝右时的数值，朝左时镜像）。角度单位是度，相对瞄准方向；屏幕坐标 y 向下，所以正角度是顺时针。
        // 三段首尾相接：-150 → 140 → -150 → 570（570 = -150 + 720，大旋转正好转两整圈，转完回到正手的起点）。
        private static readonly float[] StartAngle = { -150f, 140f, -150f };
        private static readonly float[] EndAngle = { 140f, -150f, 570f };
        // 造成伤害的时间段，用这一刀进度（0~1）表示。正手和反手是 50%~64%，大旋转是 35%~71%。
        private static readonly float[] FastStart = { 0.5f, 0.5f, 0.35f };
        private static readonly float[] FastEnd = { 0.64f, 0.64f, 0.71f };
        // 每只冤魂放出的进度点：正手、反手各 3 只，大旋转 6 只。
        private static readonly float[][] SkullTimes =
        {
            new[] { 0.53f, 0.56f, 0.6f },
            new[] { 0.53f, 0.56f, 0.6f },
            new[] { 0.4f, 0.45f, 0.5f, 0.55f, 0.6f, 0.65f },
        };

        // 虚影的暗红底色。
        private static readonly Color DarkBody = new(70, 0, 14);

        // 往回挥时竖直镜像贴图，让弯刃的一侧始终在前。
        private bool mirror;
        private int attackId;
        private int flipTimer;
        // 以下几个是“这一刀里只做一次”的标记：挥砍音效、大旋转的第二声、暗裂隙。
        private bool swingStarted;
        private bool midSpinSound;
        private bool riftSpawned;
        // 下一只要放出的冤魂序号。
        private int nextSkull;
        // 爆发开始时的刀身角度，以及虚影已经长出的比例（0~1）。
        private float burstStart;
        private float phantomSwell;
        // 最近几 tick 的刀身角度（新的在前），给拖影用。
        private readonly float[] trail = new float[TrailLength];
        private int trailCount;

        // 弹幕本身的贴图（实际绘制在 PreDraw 里自己做）。
        public override string Texture => TwoPathsHelper.TexturePath;

        private int Stage => (int)Projectile.ai[2];
        // 这一刀的进度：0 是刚出手，1 是结束。
        private float Progress => 1f - Projectile.ai[1] / Projectile.ai[0];
        private bool InFastPhase => Progress > FastStart[Stage] && Progress < FastEnd[Stage];
        private Vector2 AimDirection => Projectile.velocity.SafeNormalize(Vector2.UnitX);
        private float PhantomScale => Projectile.scale * PhantomFactor;

        public override void SafeSetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            // 每个敌人单独计无敌帧，4 tick 后可以再被同一刀打中。
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
            // 上一刀是右键（光）时，先翻转镰刀。
            if (modPlayer.LastSide == TwoPathsSide.Light)
                flipTimer = FlipTime;
            // 领一个出手编号，这一刀和它放出的冤魂、裂隙共用，同一次出手只充一次能量（见 TwoPathsPlayer）。
            if (player.whoAmI == Main.myPlayer)
                attackId = modPlayer.BeginAttack(TwoPathsSide.Dark);
        }

        // 正手、反手的挥动曲线：输入进度 x（0~1），返回已经扫过整段弧的比例。
        // 0~0.5 蓄力，轻微回拉到 -6%；0.5~0.6 爆发扫到 92%；0.6~0.633 硬停补到 100%；之后缓慢多漂 4%。
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

        // 大旋转的曲线：0~0.35 蓄力回拉到 -4%；0.35~0.42 加速到 10%；0.42~0.68 匀速高速转到 96%（两圈的主体）；
        // 0.68~0.71 硬停补到 100%；之后多漂 1%。
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

        // 每 tick 的逻辑。返回 false 跳过基类和原版的 AI，全部自己处理。
        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            if (Projectile.ai[1] <= 0f || player.dead || !player.active)
            {
                Projectile.Kill();
                return false;
            }

            // 让弹幕和玩家的出手动画持续到这一刀结束，期间不能出下一刀；heldProj 让这个弹幕作为手持物和玩家一起绘制。
            Projectile.timeLeft = (int)Projectile.ai[1] + 1;
            player.itemTime = player.itemAnimation = (int)Projectile.ai[1];
            player.heldProj = Projectile.whoAmI;
            int facing = Projectile.velocity.X >= 0f ? 1 : -1;
            player.ChangeDir(facing);

            // 按曲线算出这一 tick 的刀身角度。
            float aim = Projectile.velocity.ToRotation();
            float start = aim + MathHelper.ToRadians(StartAngle[Stage]) * facing;
            float end = aim + MathHelper.ToRadians(EndAngle[Stage]) * facing;
            float x = MathHelper.Clamp(Progress, 0f, 1f);
            float bladeRotation = MathHelper.Lerp(start, end, Curve(x));
            mirror = end < start;
            float sweep = end > start ? 1f : -1f;

            // 翻转动作：开头几 tick 额外加上半圈，逐渐收回到 0。
            bladeRotation += MathHelper.Pi * (flipTimer / (float)FlipTime) * facing;
            if (flipTimer > 0)
                flipTimer--;

            // 蓄力期间虚影不出现，爆发后在前 PhantomSwellAngle 的角度里长出来。
            if (x < FastStart[Stage])
                burstStart = bladeRotation;
            phantomSwell = MathHelper.Clamp(MathF.Abs(bladeRotation - burstStart) / PhantomSwellAngle, 0f, 1f);

            // 挥砍音效和镜头冲击比爆发提前一点点（3% 进度）触发，声音不会落后于画面。
            if (!swingStarted && x >= FastStart[Stage] - 0.03f)
            {
                swingStarted = true;
                PlaySwingSound(player);
                if (player.whoAmI == Main.myPlayer)
                    Punch(player, (aim + MathHelper.PiOver2 * sweep).ToRotationVector2(), 4f);
            }
            // 大旋转转到一半再响一声。
            if (Stage == SpinStage && !midSpinSound && x >= 0.55f)
            {
                midSpinSound = true;
                PlaySwingSound(player);
            }

            // 到了进度点就放冤魂。
            float[] skullTimes = SkullTimes[Stage];
            while (nextSkull < skullTimes.Length && x >= skullTimes[nextSkull])
            {
                // 每刀只在第一只冤魂飞出时响一声幽灵呼啸。
                if (nextSkull == 0)
                    SoundEngine.PlaySound(SoundID.DD2_GhastlyGlaivePierce with { Pitch = -0.3f, Volume = 0.55f, PitchVariance = 0.15f }, player.Center);
                nextSkull++;
                if (player.whoAmI == Main.myPlayer)
                    ReleaseSkull(player, bladeRotation);
            }

            // 大旋转的爆发结束时开暗裂隙。
            if (Stage == SpinStage && !riftSpawned && x >= FastEnd[Stage])
            {
                riftSpawned = true;
                TearRifts(player);
            }

            if (InFastPhase)
                SpawnSparks(player, bladeRotation, sweep);

            // 记录刀身角度给拖影。蓄力阶段清空记录，拖影只从爆发开始画，不包括蓄力时的回拉和翻转动作。
            if (x < FastStart[Stage])
                trailCount = 0;
            for (int i = TrailLength - 1; i > 0; i--)
                trail[i] = trail[i - 1];
            trail[0] = bladeRotation;
            trailCount = Math.Min(trailCount + 1, TrailLength);

            // 前臂指向刀身方向，弹幕中心放在前手的位置（也就是握点）。
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, bladeRotation - MathHelper.PiOver2);
            Projectile.Center = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, bladeRotation - MathHelper.PiOver2);
            Projectile.rotation = bladeRotation;
            // 统一尺寸：物品的近战尺寸加成 × SwingScale，和右键、平衡旋转、平时手持一样大。
            Projectile.scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;
            Projectile.ai[1]--;
            return false;
        }

        // 挥砍音效：两层原版音效，三段的音调依次升高。
        private void PlaySwingSound(Player player)
        {
            SoundEngine.PlaySound(SoundID.Item71 with { Pitch = Stage switch { 0 => -0.4f, 1 => -0.2f, _ => -0.05f }, PitchVariance = 0.1f }, player.Center);
            SoundEngine.PlaySound(SoundID.DD2_MonkStaffSwing with { Pitch = -0.6f, Volume = 0.8f }, player.Center);
        }

        // 冤魂从玩家胸口朝刀身当前所指的方向飞出（带一点随机偏角），所以会随着挥砍呈扇形散开。ai[0] 传出手编号。
        private void ReleaseSkull(Player player, float rotation)
        {
            Vector2 chest = player.MountedCenter + new Vector2(0f, -4f * player.gravDir);
            Vector2 velocity = rotation.ToRotationVector2().RotatedByRandom(0.2f) * 11f;
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), chest, velocity, ModContent.ProjectileType<TwoPathsSkull>(), (int)(Projectile.damage * SkullDamage), Projectile.knockBack * 0.5f, player.whoAmI, attackId);
        }

        // 大旋转终结：RiftRadius 内、视线可达的每个敌人身上各开一道方向随机的暗裂隙（长度随敌人体型），
        // 同时从角色身上向外扩散一圈暗影焰和红色火焰尘埃，标出范围。
        private void TearRifts(Player player)
        {
            SoundEngine.PlaySound(SoundID.DD2_BetsyFireballImpact with { Pitch = -0.6f, Volume = 0.9f }, player.Center);
            for (int i = 0; i < 48; i++)
            {
                Vector2 direction = (MathHelper.TwoPi * i / 48f).ToRotationVector2();
                // 速度 = 半径 / 30，尘埃大约 30 tick 飞到范围边缘。
                Dust dust = Dust.NewDustPerfect(player.Center + direction * 40f, i % 2 == 0 ? DustID.Shadowflame : DustID.CrimsonTorch, direction * RiftRadius / 30f);
                dust.noGravity = true;
                dust.scale = 1.6f;
            }

            // 镜头冲击和生成裂隙只在出手的玩家自己的客户端做。
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

        // 镜头冲击（沿 direction 方向推一下，strength 是强度，持续 14 tick）。只影响本地镜头。
        private static void Punch(Player player, Vector2 direction, float strength)
        {
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(player.Center, direction, strength, 10f, 14, 1000f, "TwoPathsDark"));
        }

        // 爆发期间沿虚影刀身后半段甩出红色火焰和暗影焰火花，方向是挥动的切线方向。
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

        // 只有爆发阶段能造成伤害（null = 按默认规则判定，false = 不造成伤害）。
        public override bool? CanDamage() => InFastPhase ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // 判定是一条从手到虚影刀尖的粗线段，跟着大虚影走。按现在的数值，长约 150 × 2.4 = 360 像素，粗约 80 × 2.4 = 192 像素。
            float collisionPoint = 0f;
            Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * TwoPathsHelper.BladeReach * PhantomScale;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, tip, HitWidth * PhantomScale, ref collisionPoint);
        }

        // 命中：充暗能量（同一刀只算一次），命中音（有限流，见 TwoPathsHelper.PlayImpactSound），轻微镜头冲击和火花。
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

        // 绘制顺序：拖影 → 虚影（两层）→ 镰刀本体。返回 false 不让原版再画一次弹幕贴图。
        public override bool PreDraw(ref Color lightColor)
        {
            // smear 是拖影和虚影的整体不透明度：爆发开始后为 1，硬停后在 SmearLinger 内降到 0，同时拖影缩回刀身。
            float smear = Progress < FastStart[Stage] ? 0f : MathHelper.Clamp(1f - (Progress - FastEnd[Stage]) / SmearLinger, 0f, 1f);
            if (smear > 0.02f)
            {
                // 虚影从本体大小长到 PhantomScale（开方让它长得先快后慢），拖影按虚影当前的大小画。
                float phantomScale = MathHelper.Lerp(Projectile.scale, PhantomScale, MathF.Sqrt(phantomSwell));
                float phantomOpacity = smear * phantomSwell;
                TwoPathsHelper.DrawSwingTrail(Projectile.Center, trail, trailCount, TwoPathsHelper.BladeReach * phantomScale, TrailAngle, smear, TwoPathsHelper.DarkTrail);
                // 虚影画两层：一层普通混合的暗红底（亮背景上也看得出），一层叠加混合的深红光。
                Color ghost = Color.Lerp(DarkBody, TwoPathsHelper.DarkCrimson, 0.35f);
                TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Dark, mirror, phantomScale, ghost * (0.45f * phantomOpacity), TwoPathsHelper.PhantomDarkPath);
                TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Dark, mirror, phantomScale, TwoPathsHelper.Additive(TwoPathsHelper.DarkCrimson, 0.4f * phantomOpacity), TwoPathsHelper.PhantomDarkPath);
            }

            // 镰刀本体：只画暗端那一半，受环境光影响但提亮 40%。
            TwoPathsHelper.DrawScythe(Projectile.Center, Projectile.rotation, TwoPathsSide.Dark, mirror, Projectile.scale, Color.Lerp(lightColor, Color.White, 0.4f));
            return false;
        }
    }
}
