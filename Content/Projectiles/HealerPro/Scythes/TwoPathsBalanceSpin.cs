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
    // 【平衡旋转】两条能量都满（平衡状态）时，左右键都会生成这个弹幕：拿出完整的双头镰，握着杆身中段持续旋转。
    // - 按住左键或右键就一直转，松开就停，剩下的能量留到下次。只在旋转时消耗，两边一起扣，满能量约转 6 秒。
    // - 合并光暗两边的特点：暗刃拖赤黑拖影、光刃拖白金拖影；
    //   光：每 0.5 秒一道审判斩穿过光标，打中的敌人身上浮现侵蚀斩痕（TwoPathsErosion）；
    //   暗：每 0.5 秒从胸口飞出两只冤魂；
    //   交汇：每 1.5 秒，640 像素内每个敌人身上开一道金边暗裂隙，再被一道审判斩交叉成 X。
    // - 能量耗尽时的终结：960 像素内所有敌人身上出现 X，玩家身上再交叉出一个贯穿全屏的大 X，重震屏，然后收回镰刀。
    // - 一次平衡的第一次旋转带“双镰合一”开场：暗、光两半从两侧转着飞到手上，合成整把镰刀，然后从静止加速进入旋转。
    //   开场由 TwoPathsPlayer.StartMerge 在进入平衡时生成（ai[1] = 1）；之后再按键生成的旋转没有开场。
    // - 旋转本身和它放出的所有弹幕都不充能（TwoPathsPlayer.BeginBalanceAttack）。
    // ai[0] = 旋转方向（1 或 -1），ai[1] = 1 表示带合一开场；velocity 存瞄准方向（单位向量）。
    // 大部分逻辑只在弹幕主人的客户端运行（读光标、扣能量、放弹幕），其他客户端只跟着画。
    public class TwoPathsBalanceSpin : ScythePro
    {
        // 转速：18 tick 一圈。
        private const float SpinSpeed = MathHelper.TwoPi / 18f;
        // 判定线比画面上的刀尖再长 10%，粗 60 × 尺寸。
        private const float HitReachFactor = 1.1f;
        private const float HitWidth = 60f;
        // 刀身打得很频繁，只用武器伤害的 35%；冤魂也是 35%；审判斩、裂隙用全额。
        private const float SpinDamage = 0.35f;
        private const float SkullDamage = 0.35f;
        // 审判斩、冤魂、X 脉冲的间隔（tick）。三者错开发出，不在同一 tick 挤在一起。
        private const int SlashInterval = 30;
        private const int SkullInterval = 30;
        private const int CrossInterval = 90;
        // X 脉冲的范围；终结时的范围、大 X 里裂隙的长度、大 X 里审判斩的放大倍数。
        private const float CrossRadius = 640f;
        private const float FinaleRadius = 960f;
        private const float FinaleRiftLength = 1400f;
        private const float FinaleSlashScale = 2.5f;
        // 拖影记录最近 9 tick 的角度，最长拖到身后约 150°。
        private const int TrailLength = 9;
        private const float TrailAngle = 2.6f;
        // 合一开场：两半飞行 20 tick，合上后 12 tick 加速到全速，开场结束后至少再转 18 tick（一圈）才响应松键。
        private const int IntroFlight = 20;
        private const int IntroSpinUp = 12;
        private const int IntroMinSpin = 18;
        // 光端那一半的出发点（相对手的位置，朝右时；朝左时左右镜像），暗端那一半从对称的另一侧出发。
        private static readonly Vector2 IntroStart = new(170f, -70f);

        // 这个弹幕已经存在了多少 tick。
        private int age;
        private int attackId;
        // 武器原本的伤害（Projectile.damage 被改成了刀身的 35%，放出的弹幕用这个全额值）。
        private int baseDamage;
        // 两端各自最近几 tick 的角度（暗端 = 光端 + 180°），给两条拖影用。
        private readonly float[] lightTrail = new float[TrailLength];
        private readonly float[] darkTrail = new float[TrailLength];
        private int trailCount;

        // 弹幕本身的贴图（实际绘制在 PreDraw 里自己做）。
        public override string Texture => TwoPathsHelper.TexturePath;

        // 从握点（杆身中段）到任一端刀尖的距离。
        private float Reach => TwoPathsHelper.FullReach * Projectile.scale;
        private bool Intro => Projectile.ai[1] == 1f;
        // 开场在第几 tick 结束（没有开场就是 0）。
        private int IntroEnd => Intro ? IntroFlight + IntroSpinUp : 0;
        // 两半是否已经合上（没有开场的旋转一开始就是合上的）。
        private bool Merged => !Intro || age >= IntroFlight;

        public override void SafeSetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            // 每个敌人单独计无敌帧，10 tick 后可以再被刀身打中。
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
            // 刀身打得很频繁，只用武器伤害的一部分；审判斩、冤魂、裂隙按 baseDamage 计算。
            baseDamage = Projectile.damage;
            Projectile.damage = (int)(baseDamage * SpinDamage);
            // 领一个“不充能”的出手编号。
            attackId = player.GetModPlayer<TwoPathsPlayer>().BeginBalanceAttack();
            SoundEngine.PlaySound((Intro ? SoundID.Item8 : SoundID.Item71) with { Pitch = -0.5f }, player.Center);
        }

        // 每 tick 的逻辑。返回 false 跳过基类和原版的 AI。
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
                // 松键就停。合一开场例外：开场一定播完，并且之后至少再转一圈，即使没有按键。
                bool held = player.controlUseItem || player.controlUseTile;
                if (!modPlayer.Balance || (!held && age >= IntroEnd + (Intro ? IntroMinSpin : 0)))
                {
                    Projectile.Kill();
                    return false;
                }

                // 开场结束后开始扣能量；两边都扣光时播放终结并收回。
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

                // 合上之后手臂跟着光标转向。两半还在飞的时候不改方向，这样它们会在原来飞向的位置合上。
                // 方向变化明显时才发网络同步，免得每 tick 都发包。
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

            // 每 tick 续命，弹幕只靠上面的条件结束；同时让玩家一直处于出手状态。
            Projectile.timeLeft = 2;
            player.itemTime = player.itemAnimation = 2;
            player.heldProj = Projectile.whoAmI;
            player.ChangeDir(Projectile.velocity.X >= 0f ? 1 : -1);

            // 手握杆身中段伸向光标，镰刀绕着手转。合一开场里：两半飞行时不转，合上后按平方曲线从静止加速到全速。
            float speed = SpinSpeed;
            if (Intro && age < IntroEnd)
                speed = age < IntroFlight ? 0f : SpinSpeed * MathF.Pow((age - IntroFlight) / (float)IntroSpinUp, 2f);
            Projectile.rotation += speed * Projectile.ai[0];
            float armRotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRotation);
            Projectile.Center = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRotation);
            // 统一尺寸：物品的近战尺寸加成 × SwingScale。
            Projectile.scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;

            // 记录两端角度给拖影。Projectile.rotation 是光刃所指的方向，暗刃在它对面。
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
                // 每转一圈（18 tick）响一声挥动音，同时最多两个实例。
                if ((age - IntroEnd) % 18 == 0)
                    SoundEngine.PlaySound(SoundID.DD2_MonkStaffSwing with { Pitch = -0.3f, Volume = 0.5f, MaxInstances = 2 }, player.Center);
                SpawnSparks();
            }
            else
            {
                // 两半飞行途中各自拖一串火花：暗端暗影焰，光端金焰。
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

        // 合一开场里两半此刻的位置，以及各自还要转多少（暗端那一半反方向转）。
        // 光端那一半从光刃将要指向的那一侧（也就是面朝的一侧）飞来，暗端那一半从身后飞来。
        // 位置按三次方缓入：开始慢、最后猛地合上。两半都还要再转 1.25 圈，合上时正好转到位。
        // lag 用来画落后几 tick 的残影。
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

        // 两半合上的那一刻：一声金属碰撞，进入平衡的特效（X 形金紫尘埃和轻震屏，见 TwoPathsPlayer.BalanceEntryEffects），
        // 镜头冲击，手上再爆一圈金紫尘埃。
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

        // 按节奏放出攻击。tick 从开场结束时开始计。
        // 光：tick 0、30、60……一道审判斩穿过光标（斜 ±45° 随机一边，ai[2] = 1 表示命中后挂侵蚀斩痕）。
        // 暗：tick 15、45、75……从胸口朝两端刀刃的方向各飞出一只冤魂。
        // 交汇：tick 45、135、225……一次 X 脉冲。
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

        // X 脉冲：radius 内、视线可达的每个敌人身上开一道金边暗裂隙（TwoPathsRift.BalanceDarkStyle），
        // 裂隙自己会在裂开后放一道审判斩交叉过去，形成 X。同时从角色身上扩散一圈金色和暗影焰尘埃标出范围，punch 是镜头冲击强度。
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

        // 能量耗尽的终结：先对更大范围（FinaleRadius）放一次 X 脉冲，再以玩家为中心交叉出一个大 X：
        // 一道 1400 像素长的金边暗裂隙（瞄准方向 +45°）和一道放大 2.5 倍的审判斩（-45°），沿两条线撒尘埃，配重震屏和音效。
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

        // 镜头冲击（沿 direction 方向推一下，持续 16 tick），只影响本地镜头。
        private static void Punch(Player player, Vector2 direction, float strength)
        {
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(player.Center, direction, strength, 10f, 16, 1000f, "TwoPathsBalance"));
        }

        // 两端刀刃沿切线甩出火花：光端金焰，暗端红色火焰。
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

        // 两半合上之前不造成伤害。
        public override bool? CanDamage() => Merged ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // 两端刀刃都能砍：判定线从一端刀尖穿过手连到另一端刀尖。
            Vector2 tip = Projectile.rotation.ToRotationVector2() * Reach * HitReachFactor;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - tip, Projectile.Center + tip, HitWidth * Projectile.scale, ref collisionPoint);
        }

        // 命中：报给能量系统（平衡期间不充能，只重置衰减计时），命中音（有限流）和金红火花。
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

        // 绘制。合一开场时画两半飞来；合上后画两条拖影和整把镰刀。返回 false 不让原版再画一次弹幕贴图。
        public override bool PreDraw(ref Color lightColor)
        {
            Color color = Color.Lerp(lightColor, Color.White, 0.5f);
            if (!Merged)
            {
                // 两半转着飞来，各带 3 个渐隐的残影（叠加混合，暗端深红、光端金色）。
                // 两半贴图和整把镰刀在同一张画布上，所以用 DrawFullScythe 画，到手上时正好拼成整把。
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
