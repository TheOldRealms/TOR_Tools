using System.Xml.Linq;
using TORTools.Core.Models;
using Xunit;

namespace TORTools.Core.Tests;

/// <summary>
/// Covers the tor_rangedweapons / tor_shields schema fields that target
/// ItemComponent/Weapon/@* via nestedPath. Verifies read + write preserve
/// the ItemComponent wrapper and the WeaponFlags child.
/// </summary>
public class NestedItemComponentRoundTripTests
{
    [Fact]
    public void GetNestedValue_ReadsAttributeOnItemComponentWeapon()
    {
        var item = XElement.Parse("""
<Item id="x" name="X">
  <ItemComponent>
    <Weapon weapon_class="Crossbow" thrust_damage="80" missile_speed="125">
      <WeaponFlags RangedWeapon="true" HasString="true" />
    </Weapon>
  </ItemComponent>
</Item>
""");
        var entry = new XmlEntry(item);

        Assert.Equal("Crossbow", entry.GetNestedValue("ItemComponent/Weapon/@weapon_class"));
        Assert.Equal("80", entry.GetNestedValue("ItemComponent/Weapon/@thrust_damage"));
        Assert.Equal("125", entry.GetNestedValue("ItemComponent/Weapon/@missile_speed"));
    }

    [Fact]
    public void SetNestedValue_UpdatesWeaponAttrAndKeepsStructureIntact()
    {
        var item = XElement.Parse("""
<Item id="x" name="X">
  <ItemComponent>
    <Weapon weapon_class="Crossbow" thrust_damage="80" missile_speed="125">
      <WeaponFlags RangedWeapon="true" HasString="true" />
    </Weapon>
  </ItemComponent>
  <Flags />
</Item>
""");
        var entry = new XmlEntry(item);

        entry.SetNestedValue("ItemComponent/Weapon/@thrust_damage", "99");
        entry.SetNestedValue("ItemComponent/Weapon/@accuracy", "88");

        var weapon = item.Element("ItemComponent")!.Element("Weapon")!;
        Assert.Equal("99", weapon.Attribute("thrust_damage")?.Value);
        Assert.Equal("88", weapon.Attribute("accuracy")?.Value);
        Assert.Equal("Crossbow", weapon.Attribute("weapon_class")?.Value);
        // WeaponFlags child survives.
        Assert.NotNull(weapon.Element("WeaponFlags"));
        Assert.Equal("true", weapon.Element("WeaponFlags")!.Attribute("RangedWeapon")?.Value);
        // Flags sibling of ItemComponent survives.
        Assert.NotNull(item.Element("Flags"));
    }

    [Fact]
    public void SetNestedValue_CreatesMissingItemComponentChain()
    {
        var item = XElement.Parse("""<Item id="x" name="X" />""");
        var entry = new XmlEntry(item);

        entry.SetNestedValue("ItemComponent/Weapon/@thrust_damage", "50");

        var weapon = item.Element("ItemComponent")?.Element("Weapon");
        Assert.NotNull(weapon);
        Assert.Equal("50", weapon!.Attribute("thrust_damage")?.Value);
    }
}
