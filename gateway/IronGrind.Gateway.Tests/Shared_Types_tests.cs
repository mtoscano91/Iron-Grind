using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Proves the shared ID types compile in IronGrind.Shared and behave identically under the gateway
/// (ADR-016 Decision 6, Authentication Story 001).
/// </summary>
[TestFixture]
public class SharedTypesTest
{
    private const uint SampleValue = 7u;
    private const int StoredValue = 42;

    [Test]
    public void test_shared_ids_round_trip_raw_value_and_equality()
    {
        Assert.That(new AccountID(SampleValue).RawValue, Is.EqualTo(SampleValue));
        Assert.That(new CharacterID(SampleValue).RawValue, Is.EqualTo(SampleValue));
        Assert.That(new ZoneID(SampleValue).RawValue, Is.EqualTo(SampleValue));

        Assert.That(new AccountID(SampleValue), Is.EqualTo(new AccountID(SampleValue)));
        Assert.That(new CharacterID(SampleValue), Is.EqualTo(new CharacterID(SampleValue)));
        Assert.That(new ZoneID(SampleValue), Is.EqualTo(new ZoneID(SampleValue)));
    }

    [Test]
    public void test_account_id_comparer_finds_dictionary_key()
    {
        var map = new Dictionary<AccountID, int>(AccountID.AccountIDComparer.Instance)
        {
            [new AccountID(SampleValue)] = StoredValue
        };

        Assert.That(map[new AccountID(SampleValue)], Is.EqualTo(StoredValue));
    }
}
