using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NodaTime;
using NUnit.Framework;
using TPP.ArgsParsing;
using TPP.ArgsParsing.TypeParsers;
using TPP.Common;
using TPP.Core.Chat;
using TPP.Core.Commands;
using TPP.Core.Commands.Definitions;
using TPP.Model;
using TPP.Persistence;

namespace TPP.Core.Tests.Commands.Definitions
{
    public class BadgeCommandsTest
    {
        private static User MockUser(string name, PkmnSpecies? selectedBadge = null) => new User(
            id: Guid.NewGuid().ToString(),
            name: name, twitchDisplayName: "☺" + name, simpleName: name.ToLower(), color: null,
            firstActiveAt: Instant.FromUnixTimeSeconds(0), lastActiveAt: Instant.FromUnixTimeSeconds(0),
            lastMessageAt: null, pokeyen: 0, tokens: 0,
            selectedBadge: selectedBadge);

        private static Message MockMessage(User user, string text = "") =>
            new(user, text, MessageSource.Chat, string.Empty);

        private Mock<IBadgeRepo> _badgeRepoMock = null!;
        private Mock<IUserRepo> _userRepoMock = null!;
        private Mock<IBadgeMarketRepo> _badgeMarketRepoMock = null!;
        private Mock<IMessageSender> _messageSender = null!;
        private ArgsParser _argsParser = null!;

        private BadgeCommands _badgeCommands = null!;

        [SetUp]
        public void SetUp()
        {
            _badgeRepoMock = new Mock<IBadgeRepo>();
            _userRepoMock = new Mock<IUserRepo>();
            _messageSender = new Mock<IMessageSender>();
            _badgeMarketRepoMock = new Mock<IBadgeMarketRepo>();
            _badgeCommands = new BadgeCommands(_badgeRepoMock.Object, _userRepoMock.Object, _badgeMarketRepoMock.Object,
                _messageSender.Object, ImmutableHashSet<PkmnSpecies>.Empty);
            _argsParser = new ArgsParser();
            _argsParser.AddArgumentParser(new OptionalParser(_argsParser));
            _argsParser.AddArgumentParser(new UserParser(_userRepoMock.Object));
            _argsParser.AddArgumentParser(new AnyOrderParser(_argsParser));
            _argsParser.AddArgumentParser(new PositiveIntParser());
            _argsParser.AddArgumentParser(new BadgeSourceParser());
            _argsParser.AddArgumentParser(new FormParser());
            _argsParser.AddArgumentParser(new ShinyParser());
        }

        [TestFixture]
        private class Badges : BadgeCommandsTest
        {
            [Test]
            public async Task TestBadgesSelf()
            {
                User user = MockUser("MockUser");
                var species1 = PkmnSpecies.RegisterName("1", "Einsmon");
                var species2 = PkmnSpecies.RegisterName("22", "Zwozwomon");
                var species3 = PkmnSpecies.RegisterName("13", "Drölfmon");

                Badge badge1 = new Badge("1", user.Id, species1, Badge.BadgeSource.ManualCreation, Instant.FromUtc(1, 1, 1, 1, 1), null, false);
                Badge badge2 = new Badge("2", user.Id, species2, Badge.BadgeSource.ManualCreation, Instant.FromUtc(1, 1, 1, 1, 1), "hisuian", false);
                Badge badge3 = new Badge("3", user.Id, species3, Badge.BadgeSource.ManualCreation, Instant.FromUtc(1, 1, 1, 1, 1), null, true);
                _badgeRepoMock.Setup(repo => repo.FindAllByCustom(user.Id, null, null, null, false)).Returns(
                    Task.FromResult(new List<Badge>() { badge1, badge2, badge2, badge3, badge3, badge3 }));

                _userRepoMock
                    .Setup(repo => repo.FindBySimpleName(user.SimpleName))
                    .ReturnsAsync(user);

                CommandResult result = await _badgeCommands.Badges(new CommandContext(MockMessage(user),
                    ImmutableList<string>.Empty, _argsParser));

                const string response = "Your badges: 1x admin created #001 Einsmon, 3x admin created shiny #013 Drölfmon, 2x admin created hisuian #022 Zwozwomon";
                Assert.That(result.Response, Is.EqualTo(response));
            }

            [Test]
            public async Task TestBadgesOther()
            {
                User user = MockUser("MockUser");
                User otherUser = MockUser("Someone_Else");
                var species1 = PkmnSpecies.RegisterName("1", "Einsmon");
                var species2 = PkmnSpecies.RegisterName("22", "Zwozwomon");
                var species3 = PkmnSpecies.RegisterName("13", "Drölfmon");

                Badge badge1 = new Badge("1", otherUser.Id, species1, Badge.BadgeSource.ManualCreation, Instant.MinValue, null, false);
                Badge badge2 = new Badge("2", otherUser.Id, species2, Badge.BadgeSource.ManualCreation, Instant.MinValue, "hisuian", false);
                Badge badge3 = new Badge("3", otherUser.Id, species3, Badge.BadgeSource.ManualCreation, Instant.MinValue, null, true);

                _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species1, species2, species3 }));
                _userRepoMock
                    .Setup(repo => repo.FindBySimpleName(otherUser.SimpleName))
                    .ReturnsAsync(otherUser);

                _badgeRepoMock.Setup(repo => repo.FindAllByCustom(otherUser.Id, null, null, null, false)).Returns(
                    Task.FromResult(new List<Badge>() { badge1, badge2, badge3 }));

                CommandResult result = await _badgeCommands.Badges(new CommandContext(MockMessage(user),
                    ImmutableList.Create("sOmeOnE_eLsE"), _argsParser));

                const string response =
                    "Someone_Else's badges: 1x admin created #001 Einsmon, 1x admin created shiny #013 Drölfmon, 1x admin created hisuian #022 Zwozwomon";
                Assert.That(result.Response, Is.EqualTo(response));
            }

            [Test]
            public void TestBadgesUserNotFound()
            {
                _argsParser.AddArgumentParser(new PkmnSpeciesParser(Array.Empty<PkmnSpecies>()));
                User user = MockUser("MockUser");
                ArgsParseFailure exception = Assert.ThrowsAsync<ArgsParseFailure>(() => _badgeCommands.Badges(
                    new CommandContext(MockMessage(user), ImmutableList.Create("@someone_unknown"), _argsParser)))!;
                Assert.That(exception.Message, Is.EqualTo("did not recognize a user with the name 'someone_unknown'"));
            }

            [Test]
            public async Task TestSpeciesOverNameIfAmbiguous()
            {
                User user = MockUser("MockUser");
                PkmnSpecies species = PkmnSpecies.RegisterName("1", "PersonMon");
                Badge badge = new Badge("", user.Id, species, Badge.BadgeSource.RunCaught, Instant.MinValue, null, false);
                _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
                User otherUser = MockUser("PersonMon");
                _userRepoMock
                    .Setup(repo => repo.FindBySimpleName(otherUser.SimpleName))
                    .ReturnsAsync(otherUser);
                _badgeRepoMock.Setup(repo => repo.FindAllByCustom(user.Id, species, null, null, false)).Returns(
                    Task.FromResult(new List<Badge>() { badge }));
                _badgeRepoMock.Setup(repo => repo.FindAllByCustom(otherUser.Id, null, null, null, false)).Returns(
                    Task.FromResult(new List<Badge>()));


                CommandResult resultAmbiguous = await _badgeCommands.Badges(new CommandContext(MockMessage(user),
                    ImmutableList.Create("PersonMon"), _argsParser));
                Assert.That(resultAmbiguous.Response, Is.EqualTo("Your badges: 1x run caught #001 PersonMon"));

                CommandResult resultDisambiguated = await _badgeCommands.Badges(new CommandContext(MockMessage(user),
                    ImmutableList.Create("@PersonMon"), _argsParser));
                Assert.That(resultDisambiguated.Response, Is.EqualTo("PersonMon has no badges."));
            }

            [Test]
            public async Task TestSpeciesAndUserInAnyOrder()
            {
                User user = MockUser("User");
                PkmnSpecies species = PkmnSpecies.RegisterName("1", "Species");
                Badge badge = new Badge("", user.Id, species, Badge.BadgeSource.Pinball, Instant.MinValue, null, false);
                _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
                _userRepoMock
                    .Setup(repo => repo.FindBySimpleName(user.SimpleName))
                    .ReturnsAsync(user);
                _badgeRepoMock
                    .Setup(repo => repo.CountByUserAndSpecies(user.Id, species))
                    .ReturnsAsync(1);
                _badgeRepoMock
                    .Setup(repo => repo.CountByUserPerSpecies(user.Id))
                    .ReturnsAsync(ImmutableSortedDictionary<PkmnSpecies, int>.Empty);
                _badgeRepoMock.Setup(repo => repo.FindAllByCustom(user.Id, species, null, null, false)).Returns(
                    Task.FromResult(new List<Badge>() { badge }));
                _badgeRepoMock.Setup(repo => repo.FindAllByCustom(user.Id, null, null, null, false)).Returns(
                    Task.FromResult(new List<Badge>() { badge }));

                CommandResult result1 = await _badgeCommands.Badges(new CommandContext(MockMessage(user),
                    ImmutableList.Create("Species", "User"), _argsParser));
                CommandResult result2 = await _badgeCommands.Badges(new CommandContext(MockMessage(user),
                    ImmutableList.Create("User", "Species"), _argsParser));

                Assert.That(result2.Response, Is.EqualTo(result1.Response));
                Assert.That(result1.Response, Is.EqualTo("User's badges: 1x pinball caught #001 Species"));
            }
        }

        [Test]
        public async Task TestUnselectBadge()
        {
            PkmnSpecies species = PkmnSpecies.RegisterName("1", "Mon");
            User user = MockUser("MockUser", selectedBadge: species);

            CommandResult result = await _badgeCommands.UnselectBadge(new CommandContext(MockMessage(user),
                ImmutableList<string>.Empty, _argsParser));

            Assert.That(result.Response, Is.EqualTo("#001 Mon badge unequipped."));
            _userRepoMock.Verify(repo => repo.SetSelectedBadge(user, null), Times.Once());
        }

        [Test]
        public async Task TestUnselectBadgeNoneEquipped()
        {
            User user = MockUser("MockUser", selectedBadge: null);

            CommandResult result = await _badgeCommands.UnselectBadge(new CommandContext(MockMessage(user),
                ImmutableList<string>.Empty, _argsParser));

            Assert.That(result.Response, Is.EqualTo("You don't have a badge equipped."));
            _userRepoMock.VerifyNoOtherCalls();
        }

        [Test]
        public async Task TestSelectBadge()
        {
            User user = MockUser("MockUser");
            PkmnSpecies species = PkmnSpecies.RegisterName("1", "Mon");
            _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
            _badgeRepoMock.Setup(repo => repo.HasUserBadge(user.Id, species)).ReturnsAsync(true);

            CommandResult result = await _badgeCommands.SelectBadge(new CommandContext(MockMessage(user),
                ImmutableList.Create("#1"), _argsParser));

            _userRepoMock.Verify(repo => repo.SetSelectedBadge(user, species), Times.Once());
            Assert.That(result.Response, Is.EqualTo("#001 Mon selected as badge."));
        }

        [Test]
        public async Task TestSelectBadgeNotOwner()
        {
            User user = MockUser("MockUser");
            PkmnSpecies species = PkmnSpecies.RegisterName("1", "Mon");
            _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
            _badgeRepoMock.Setup(repo => repo.HasUserBadge(user.Id, species)).ReturnsAsync(false);

            CommandResult result = await _badgeCommands.SelectBadge(new CommandContext(MockMessage(user),
                ImmutableList.Create("#1"), _argsParser));

            _userRepoMock.VerifyNoOtherCalls();
            Assert.That(result.Response, Is.EqualTo("#001 Mon is not an owned badge."));
        }

        [Test]
        public void TestSelectUnknownBadge()
        {
            _argsParser.AddArgumentParser(new PkmnSpeciesParser(Array.Empty<PkmnSpecies>()));
            User user = MockUser("MockUser");
            ArgsParseFailure failure = Assert.ThrowsAsync<ArgsParseFailure>(() =>
                _badgeCommands.SelectBadge(new CommandContext(MockMessage(user),
                    ImmutableList.Create("#123"), _argsParser)))!;
            Assert.That(failure.Message, Is.EqualTo("did not recognize species '#123'"));
        }

        [Test]
        public async Task TestPokedex()
        {
            User user = MockUser("MockUser");
            _badgeRepoMock
                .Setup(repo => repo.CountByUserPerSpecies(user.Id))
                .ReturnsAsync(new Dictionary<PkmnSpecies, int>
                {
                    [PkmnSpecies.OfId("1")] = 12,
                    [PkmnSpecies.OfId("2")] = 23,
                    [PkmnSpecies.OfId("3")] = 34,
                }.ToImmutableSortedDictionary());

            CommandResult result = await _badgeCommands.Pokedex(new CommandContext(MockMessage(user),
                ImmutableList<string>.Empty, _argsParser));

            Assert.That(result.Response, Is.EqualTo("You have collected 3 distinct Pokémon badge(s)"));
        }

        [Test]
        public async Task TestGiftBadgeSuccessful()
        {
            PkmnSpecies species = PkmnSpecies.RegisterName("1", "species");
            _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
            User user = MockUser("MockUser");
            User recipient = MockUser("Recipient");
            _userRepoMock.Setup(repo => repo.FindBySimpleName("recipient")).Returns(Task.FromResult((User?)recipient));
            Badge badge1 = new("badge1", user.Id, species, Badge.BadgeSource.ManualCreation, Instant.MinValue, null, false);
            Badge badge2 = new("badge2", user.Id, species, Badge.BadgeSource.ManualCreation, Instant.MinValue, null, false);
            Badge badge3 = new("badge3", user.Id, species, Badge.BadgeSource.ManualCreation, Instant.MinValue, null, false);
            _badgeRepoMock.Setup(repo => repo.FindAllByCustom(user.Id, species, null, null, false))
                .Returns(Task.FromResult(new List<Badge> { badge1, badge2, badge3, }));

            CommandResult result = await _badgeCommands.GiftBadge(new CommandContext(MockMessage(user),
                ImmutableList.Create("recipient", "species", "2"), _argsParser));

            Assert.That(result.Response, Is.EqualTo("has gifted 2 #001 species badges to Recipient!"));
            Assert.That(result.ResponseTarget, Is.EqualTo(ResponseTarget.Chat));
            IDictionary<string, object?> data = new Dictionary<string, object?> { ["gifter"] = user.Id };
            _badgeRepoMock.Verify(repo => repo.TransferBadges(
                It.Is<IImmutableList<Badge>>(list => list.SequenceEqual(ImmutableList.Create(badge1, badge2))),
                recipient.Id,
                "gift",
                It.Is<IDictionary<string, object?>>(dict => dict.DictionaryEqual(data))));
        }

        [Test]
        public async Task TestGiftBadgeNotOwned()
        {
            PkmnSpecies species = PkmnSpecies.RegisterName("1", "species");
            _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
            User user = MockUser("MockUser");
            User recipient = MockUser("Recipient");
            _userRepoMock.Setup(repo => repo.FindBySimpleName("recipient")).Returns(Task.FromResult((User?)recipient));
            _badgeRepoMock.Setup(repo => repo.FindAllByCustom(user.Id, species, null, null, false))
                .Returns(Task.FromResult(new List<Badge>()));

            CommandResult result = await _badgeCommands.GiftBadge(new CommandContext(MockMessage(user),
                ImmutableList.Create("recipient", "species"), _argsParser));

            Assert.That(result.Response, Is.EqualTo("You tried to gift 1 #001 species badges, but you only have 0."));
            Assert.That(result.ResponseTarget, Is.EqualTo(ResponseTarget.Source));
            _badgeRepoMock.Verify(repo => repo.TransferBadges(
                    It.IsAny<IImmutableList<Badge>>(),
                    It.IsAny<string?>(),
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, object?>>()),
                Times.Never);
        }

        [Test]
        public async Task ListSellBadge_lists_callers_badges_when_user_isnt_specified()
        {
            User user1 = MockUser("u1");
            PkmnSpecies speciesA = PkmnSpecies.RegisterName("1", "a");
            Badge badgeA = new("badgeA", user1.Id, speciesA, Badge.BadgeSource.Pinball, Instant.MinValue, null, false) { SellPrice = 1 };

            _badgeRepoMock.Setup(repo => repo.FindAllForSaleByCustom(user1.Id, speciesA, null, null, false))
                .Returns(Task.FromResult(new List<Badge>() { badgeA }));
            _userRepoMock.Setup(repo => repo.FindById(user1.Id)).Returns(Task.FromResult((User?)user1));

            _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { speciesA }));

            CommandResult result = await _badgeCommands.ListSellBadge(new CommandContext(MockMessage(user1), ImmutableList.Create("a"), _argsParser));

            Assert.That(result.Response, Is.EqualTo("1 badges found: pinball caught #001 a sold by u1 for T1"));
        }

        [Test]
        public async Task ListSellBadge_lists_others_sold_badges()
        {
            User caller = MockUser("streamer");
            User seller = MockUser("notstreamer");
            PkmnSpecies species = PkmnSpecies.OfId("69");
            Badge.BadgeSource source = Badge.BadgeSource.Transmutation;
            string? form = null;
            bool shiny = false;
            Badge badgeA = new Badge("A", seller.Id, species, source, Instant.MinValue, form, shiny) { SellPrice = 1 };
            Badge badgeB = new Badge("B", seller.Id, species, source, Instant.MinValue, form, shiny) { SellPrice = 1 };

            _argsParser.AddArgumentParser(new PkmnSpeciesParser(new[] { species }));
            _userRepoMock.Setup(repo => repo.FindBySimpleName("notstreamer")).Returns(Task.FromResult((User?)seller));
            _userRepoMock.Setup(repo => repo.FindById(seller.Id)).Returns(Task.FromResult((User?)seller));
            _badgeMarketRepoMock.Setup(repo => repo.FindAllBadgesForSale(seller.Id, null, null, null, shiny))
                .Returns(Task.FromResult(new List<Badge>() { badgeA, badgeB }));

            CommandResult result = await _badgeCommands.ListSellBadge(new CommandContext(MockMessage(caller), ImmutableList.Create("notstreamer"), _argsParser));

            Assert.That(result.Response, Is.EqualTo("2 badges found: 2x transmuted #069 ??? sold by notstreamer for T1"));
        }
    }
}
