using System.Globalization;
using IronGrind.Authentication;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 005: credential validation (AC-AUTH-11, 13, 28, 35 and CR-AUTH-8 step 2 order).</summary>
[TestFixture]
internal sealed class Auth_CredentialValidation_tests
{
    private const string USERNAME = "alice_login";
    private const string GOOD_PASSWORD = "correct horse";
    private const string GOOD_NAME = "Grim_Iron7";
    private const byte WARRIOR = 0;
    private const byte HEALER = 1;
    private const string SMILE = "\U0001F600";

    private static readonly AuthOptions OPTIONS = new();

    private static RegistrationError? Username(string raw)
    {
        return UsernameRules.Validate(UsernameRules.Normalize(raw), OPTIONS);
    }

    private static RegistrationError? Name(string name)
    {
        return CharacterNameRules.Validate(name, USERNAME, OPTIONS);
    }

    private static RegistrationError? Register(string user, string pass, string name, byte cls)
    {
        return new RegistrationValidator(OPTIONS).Validate(new RegistrationRequest(user, pass, name, cls));
    }

    // AC-1

    [Test]
    public void test_username_two_chars_returns_too_short()
    {
        Assert.That(Username("ab"), Is.EqualTo(RegistrationError.UsernameTooShort));
    }

    [Test]
    public void test_username_25_chars_returns_too_long()
    {
        Assert.That(Username(new string('a', 25)), Is.EqualTo(RegistrationError.UsernameTooLong));
    }

    [TestCase("ab!")]
    [TestCase("a b")]
    public void test_username_bad_chars_returns_invalid_chars(string raw)
    {
        Assert.That(Username(raw), Is.EqualTo(RegistrationError.UsernameInvalidChars));
    }

    [TestCase("abc")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaa")]
    [TestCase("alice_0")]
    [TestCase("ALICE")]
    public void test_username_valid_returns_null(string raw)
    {
        Assert.That(Username(raw), Is.Null);
    }

    [Test]
    public void test_username_normalize_uppercase_lowercases()
    {
        Assert.That(UsernameRules.Normalize("ALICE"), Is.EqualTo("alice"));
    }

    [Test]
    public void test_username_normalize_turkish_culture_is_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.That(UsernameRules.Normalize("I"), Is.EqualTo("i"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // AC-2

    [TestCase("\u0430lice")]
    [TestCase("\uFF41lice")]
    [TestCase("\u00E0lice")]
    [TestCase("\u65E5\u672C\u8A9E")]
    public void test_username_homoglyph_returns_invalid_chars(string raw)
    {
        Assert.That(Username(raw), Is.EqualTo(RegistrationError.UsernameInvalidChars));
    }

    [Test]
    public void test_username_13_four_byte_chars_counts_code_points()
    {
        string username = string.Concat(Enumerable.Repeat(SMILE, 13));

        Assert.That(username.Length, Is.EqualTo(26));
        Assert.That(Username(username), Is.EqualTo(RegistrationError.UsernameInvalidChars));
    }

    // AC-3

    [Test]
    public void test_password_seven_chars_returns_too_short()
    {
        Assert.That(PasswordRules.Validate(new string('p', 7), OPTIONS), Is.EqualTo(RegistrationError.PasswordTooShort));
    }

    [Test]
    public void test_password_73_chars_returns_too_long()
    {
        Assert.That(PasswordRules.Validate(new string('p', 73), OPTIONS), Is.EqualTo(RegistrationError.PasswordTooLong));
    }

    [TestCase(8)]
    [TestCase(72)]
    public void test_password_boundary_lengths_return_null(int length)
    {
        Assert.That(PasswordRules.Validate(new string('p', length), OPTIONS), Is.Null);
    }

    [Test]
    public void test_password_72_four_byte_chars_counts_code_points()
    {
        string password = string.Concat(Enumerable.Repeat(SMILE, 72));

        Assert.That(password.Length, Is.EqualTo(144));
        Assert.That(PasswordRules.Validate(password, OPTIONS), Is.Null);
    }

    [Test]
    public void test_password_73_four_byte_chars_returns_too_long()
    {
        string password = string.Concat(Enumerable.Repeat(SMILE, 73));

        Assert.That(PasswordRules.Validate(password, OPTIONS), Is.EqualTo(RegistrationError.PasswordTooLong));
    }

    // AC-4

    [Test]
    public void test_name_two_chars_returns_too_short()
    {
        Assert.That(Name("ab"), Is.EqualTo(RegistrationError.CharacterNameTooShort));
    }

    [Test]
    public void test_name_17_chars_returns_too_long()
    {
        Assert.That(Name(new string('a', 17)), Is.EqualTo(RegistrationError.CharacterNameTooLong));
    }

    [TestCase("Bad Name")]
    [TestCase("\u00C9van")]
    [TestCase("\u0430lex")]
    [TestCase("ab!")]
    public void test_name_bad_chars_returns_invalid_chars(string name)
    {
        Assert.That(Name(name), Is.EqualTo(RegistrationError.CharacterNameInvalidChars));
    }

    [Test]
    public void test_name_short_and_invalid_returns_too_short_first()
    {
        Assert.That(Name("a!"), Is.EqualTo(RegistrationError.CharacterNameTooShort));
    }

    [TestCase("ALICE_LOGIN")]
    [TestCase("Alice_Login")]
    public void test_name_equals_username_ignoring_case_returns_matches_username(string name)
    {
        Assert.That(Name(name), Is.EqualTo(RegistrationError.CharacterNameMatchesUsername));
    }

    [TestCase("abc")]
    [TestCase("aaaaaaaaaaaaaaaa")]
    [TestCase("Grim_Iron7")]
    public void test_name_valid_returns_null(string name)
    {
        Assert.That(Name(name), Is.Null);
    }

    [Test]
    public void test_name_9_four_byte_chars_counts_code_points()
    {
        string name = string.Concat(Enumerable.Repeat(SMILE, 9));

        Assert.That(name.Length, Is.EqualTo(18));
        Assert.That(Name(name), Is.EqualTo(RegistrationError.CharacterNameInvalidChars));
    }

    // AC-5

    [Test]
    public void test_register_all_bad_returns_username_error_first()
    {
        Assert.That(Register("ab", "abc", "Bad Name", 2), Is.EqualTo(RegistrationError.UsernameTooShort));
    }

    [Test]
    public void test_register_username_fixed_returns_password_error()
    {
        Assert.That(Register(USERNAME, "abc", "Bad Name", 2), Is.EqualTo(RegistrationError.PasswordTooShort));
    }

    [Test]
    public void test_register_password_fixed_returns_character_name_error()
    {
        Assert.That(Register(USERNAME, GOOD_PASSWORD, "Bad Name", 2), Is.EqualTo(RegistrationError.CharacterNameInvalidChars));
    }

    [Test]
    public void test_register_name_fixed_returns_class_invalid()
    {
        Assert.That(Register(USERNAME, GOOD_PASSWORD, GOOD_NAME, 2), Is.EqualTo(RegistrationError.ClassInvalid));
    }

    [Test]
    public void test_register_class_255_returns_class_invalid()
    {
        Assert.That(Register(USERNAME, GOOD_PASSWORD, GOOD_NAME, 255), Is.EqualTo(RegistrationError.ClassInvalid));
    }

    [TestCase(WARRIOR)]
    [TestCase(HEALER)]
    public void test_register_playable_class_returns_null(byte cls)
    {
        Assert.That(Register(USERNAME, GOOD_PASSWORD, GOOD_NAME, cls), Is.Null);
    }

    [Test]
    public void test_register_name_matches_uppercase_username_returns_matches_username()
    {
        Assert.That(Register("ALICE_LOGIN", GOOD_PASSWORD, "Alice_Login", WARRIOR),
            Is.EqualTo(RegistrationError.CharacterNameMatchesUsername));
    }
}
