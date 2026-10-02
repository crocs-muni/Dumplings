using Dumplings.Rpc;
using Dumplings.Scanning;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Dumplings.Tests
{
    public class OtherCoinJoinDetectionTests
    {
        private const long V = 1_000_000; // Equal output value of the synthetic coinjoins.

        private static readonly Func<uint256, bool> NoKnownCoinJoins = _ => false;

        [Fact]
        public void AcceptsMakerEarningSmallFee()
        {
            // Maker A holds the largest input and the largest change and earns 1,500 sat.
            var tx = Tx(
                new[] { In(V + 4_000_000 - 1_500), In(1_500_000), In(2_000_000), In(1_200_000) },
                new[] { Out(V), Out(V), Out(V), Out(V), Out(4_000_000), Out(501_500), Out(1_001_500), Out(193_500) });

            Assert.True(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void AcceptsTakerWithLargestChange()
        {
            // Taker pays 3,000 sat to makers and 47,000 sat mining fee.
            var tx = Tx(
                new[] { In(V + 3_000_000 + 50_000), In(1_500_000), In(2_000_000), In(1_200_000) },
                new[] { Out(V), Out(V), Out(V), Out(V), Out(3_000_000), Out(501_000), Out(1_001_000), Out(201_000) });

            Assert.True(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void AcceptsTakerSweep()
        {
            // n equal outputs, n - 1 changes: the taker has no change.
            var tx = Tx(
                new[] { In(V + 10_000), In(1_500_000), In(2_000_000), In(1_200_000) },
                new[] { Out(V), Out(V), Out(V), Out(V), Out(501_000), Out(1_001_000), Out(201_000) });

            Assert.True(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void RejectsTwoPartyLookAlike()
        {
            // Would pass the max-input bound, but two equal outputs is not taker + two makers.
            var tx = Tx(
                new[] { In(10_000), In(20_000) },
                new[] { Out(330), Out(330), Out(9_500), Out(19_500) });

            Assert.False(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void AcceptsHighFeeTakerWithTwoParentCoinJoins()
        {
            var (tx, parent1, parent2) = HighFeeTaker();

            Assert.False(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins)); // Above the max-input bound on its own.
            Assert.True(Scanner.IsOtherCoinJoin(tx, new HashSet<uint256> { parent1, parent2 }.Contains));
        }

        [Fact]
        public void RejectsHighFeeTakerWithOneParentCoinJoin()
        {
            var (tx, parent1, _) = HighFeeTaker();

            Assert.False(Scanner.IsOtherCoinJoin(tx, new HashSet<uint256> { parent1 }.Contains));
        }

        [Fact]
        public void RejectsConsolidationOfManyParentCoinJoins()
        {
            var parents = Enumerable.Range(0, 50).Select(_ => RandomUtils.GetUInt256()).ToArray();
            var tx = Tx(
                parents.Select(x => In(V, x)).ToArray(),
                new[] { Out(50 * V - 20_000) });

            Assert.False(Scanner.IsOtherCoinJoin(tx, new HashSet<uint256>(parents).Contains));
        }

        [Fact]
        public void RejectsOversizedInput()
        {
            var tx = Tx(
                new[] { In(1_500_000), In(2_000_000), In(Money.Coins(10).Satoshi) },
                new[] { Out(V), Out(V), Out(V), Out(500_000), Out(1_000_000), Out(600_000) });

            Assert.False(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void RejectsWhirlpoolTx0Shape()
        {
            // Single input ≈ n·v + change, n premix outputs, coordinator fee, OP_RETURN and change.
            const long premix = V + 170;
            var tx = Tx(
                new[] { In(5 * premix + 42_500 + 2_000_000 + 1_000) },
                new[]
                {
                    Out(premix), Out(premix), Out(premix), Out(premix), Out(premix),
                    Out(42_500),
                    new VerboseOutputInfo(Money.Zero, TxNullDataTemplate.Instance.GenerateScriptPubKey(new byte[46])),
                    Out(2_000_000)
                });

            Assert.False(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void FeeAllowanceArithmetic()
        {
            Assert.Equal(Money.Coins(0.001m), Scanner.OtherCoinJoinFeeAllowance(Money.Coins(0.01m))); // Fixed floor wins.
            Assert.Equal(Money.Coins(0.05m), Scanner.OtherCoinJoinFeeAllowance(Money.Coins(10m))); // Relative wins.
        }

        [Fact]
        public void AcceptsMainnetJoinMarket9cb79d56()
        {
            // Block 867013. The largest-input maker earns 15 sat, which the former "- 0.0001 BTC" margin rejected.
            var tx = new VerboseTransactionInfo(
                null,
                uint256.Parse("9cb79d566256bea130eb88ee0f12a85617bc166f17f49a2e8a6edd8365b94298"),
                new[]
                {
                    In("56d8c0e66ed73b36bcc6e3a566b6118b8870e26bf298500c0102d464414ce788", 15, 2411000, "0014f7a77d9545f6ecfb1ff20b6a63dd396a1f3e1afc"),
                    In("804af67c4be3b4cfed1cac4834a48b3666b98c13f10141f6ebad1227d493d294", 23, 175813, "0014de4813e4ba6adc77d6ef216f84389d5d23ca06fe"),
                    In("9e154ce9ef670eefd02ca1532a836b8f0d2d5d1eeb6393ed79e2b4884a13b1d6", 13, 7526469, "00141053643fc8e48cb827fa9befd6f7ad8fa6b6f7cb"),
                    In("359eb73daebc98eb96d401beab1f9ef7592217a65600a21cd3fbff4a4edd98e6", 0, 7412750, "0014e4ef62ea364a30de83f94af2c35384a8015bf134"),
                    In("2ae42dcd1b2e9c6918493acc4373122f04cdb7d9e38e24a1226539470fc00f54", 0, 1498872, "001409c76d15068d8f6a46c3fd5f858df77cd5b72b39"),
                    In("47c3c68bd50de4dd505e748d2c7d4b619e410f065897bcf97f789f8886c56509", 6, 1326535, "001451f118685610ab115c7eb3f8e4090d18234c6144"),
                    In("19d4223b5036258ecf98aef2714ec119774f9a6272b7dd57ae2131a07a7a879e", 0, 3468740, "001492e76f2f0e7e2485943d543896af56ce81e16c10"),
                    In("ad8e420c69544872221d9af17156e653a40f78d1560b8e74c14b6a84476d75b2", 12, 600000, "0014061e4c3bc95ce2fc16c969291eb74f684636c435"),
                    In("0084b2aa874b34f6401faa68f55a9d85774f73adab245b1169eed7bbfb639147", 2, 55896, "00140ae397be23c18308a2916d504e7a5a031554ee14"),
                    In("19d4223b5036258ecf98aef2714ec119774f9a6272b7dd57ae2131a07a7a879e", 13, 36372910, "00144ed58df5d9df4327a28856e33e5a63b4efc7c224"),
                    In("289a09b67ef89edcf7f55f8e4e98f5aeea7d3617766769e51b3d3ecc7c8b7919", 2, 2423396, "0014d6716b3681a9be7591ddabcdf08f0cb17fff584f"),
                    In("74aa90dcd48ec3a59aed3c4c0e490518203bf4a0f82d7003190db6172e02791f", 17, 700000, "0014770452eb67584d7d79d87ed0ee1354f4f35bae50"),
                    In("97f04f0f63f5dd25031ade67262fdccac7980393a9e5a661d00753a4d630186c", 13, 590707, "0014d5a62effca01bf5403603f0373eaec39025a7bc6"),
                },
                new[]
                {
                    Out(1483226, "00149cf1b54e0b515053a1191b7c3794f068afcdb846"),
                    Out(75085, "00146c10fcf687281fcddfb9403f2e2c2cf0c00ef54d"),
                    Out(1483226, "0014084b604bc6f516e9131222022bec6e1d1356b40e"),
                    Out(1483226, "001473551702c9dcb9c0fa7bde1c7519a6ad0d0dfd57"),
                    Out(1483226, "00140250037f803e357971f734cf759edf31a25cf618"),
                    Out(1985662, "001417cc786bfee9bfcc6802a6ee2b66ea943e3f1718"),
                    Out(5931945, "00143feca0d5cec83934c9ee71210d15b91d6edf12ad"),
                    Out(34889699, "00141766958d6a748e8aafa05d613f2518ea4902c848"),
                    Out(1483226, "0014de773fe7e774712be2afce477b7d95c3e79fa025"),
                    Out(1483226, "00144a3c200d450f9883ebe559427e7bf603105f6c4f"),
                    Out(927921, "0014f882a970c2cc3226b4eaf48a5c9cf7c18d536d69"),
                    Out(940198, "001439371599276e2b0d2b3ee754103e99aabc3a0046"),
                    Out(1483226, "00149bada7e32bd09bccf6b47de1c57548d13ad6a49f"),
                    Out(6043307, "00141370d2f8f22aac1605d69813cd8ac2334cd90b5b"),
                    Out(1483226, "00144b5a2e83df007197e5b7b30e4029a424a0ea414c"),
                    Out(1483226, "0014369ab7c5e949a3ac94d8570b449a90247edac6c7"),
                    Out(407926, "0014f1d69147302d42e39b0c14e6df5fed437728c27e"),
                });

            Assert.True(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        [Fact]
        public void RejectsMainnetTwoPartyB61e05ce()
        {
            // Block 733936. Only two equal outputs.
            var tx = new VerboseTransactionInfo(
                null,
                uint256.Parse("b61e05cee65742dfdc374fa08d9b07603d610f0e825813a8a5cc1a47c2ff2624"),
                new[]
                {
                    In("1e054b4b76e8f5fd2d7191ec110db6de98032a4304f443c5c165e0e43d414f60", 2, 257423, "00145b8154c451af4acabf666b3f98612d26085df119"),
                    In("fab6e60dd1f563f7f49b52dd88462f7c0afe4e269c48cdd90b3a0671c3e60522", 2, 271230, "0014b48d7cbb44e7405834f4615d80e360370ef12e70"),
                },
                new[]
                {
                    Out(4472, "00148b1fe55ea929b548b3989837f73e07d9c2f10164"),
                    Out(18279, "00146da0ba0bbcf493f24f2a41318f49d4b5ffd09e7c"),
                    Out(252323, "001459235d73c8e24956592ef3c6e348838e44502847"),
                    Out(252323, "0014f24ee014c1e2e491719dae9caea30ec47e4dfbd5"),
                });

            Assert.False(Scanner.IsOtherCoinJoin(tx, NoKnownCoinJoins));
        }

        /// <summary>
        /// Structurally valid 3-party coinjoin whose taker pays 600,000 sat in fees, so its input exceeds the max-input bound.
        /// The makers' inputs come from two different parent coinjoins.
        /// </summary>
        private static (VerboseTransactionInfo tx, uint256 parent1, uint256 parent2) HighFeeTaker()
        {
            var parent1 = RandomUtils.GetUInt256();
            var parent2 = RandomUtils.GetUInt256();
            var tx = Tx(
                new[] { In(V + 2_000_000 + 600_000), In(1_500_000, parent1), In(2_000_000, parent2) },
                new[] { Out(V), Out(V), Out(V), Out(2_000_000), Out(501_000), Out(1_001_000) });
            return (tx, parent1, parent2);
        }

        private static VerboseTransactionInfo Tx(VerboseInputInfo[] inputs, VerboseOutputInfo[] outputs)
            => new(null, RandomUtils.GetUInt256(), inputs, outputs);

        private static VerboseInputInfo In(long value, uint256 parent = null)
            => new(new OutPoint(parent ?? RandomUtils.GetUInt256(), 0), Out(value), uint.MaxValue);

        private static VerboseInputInfo In(string parent, uint n, long value, string scriptHex)
            => new(new OutPoint(uint256.Parse(parent), n), Out(value, scriptHex), uint.MaxValue);

        private static VerboseOutputInfo Out(long value)
            => new(Money.Satoshis(value), new Key().PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit));

        private static VerboseOutputInfo Out(long value, string scriptHex)
            => new(Money.Satoshis(value), Script.FromHex(scriptHex));
    }
}
