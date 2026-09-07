within ;
package Smoke "A deliberately imperfect library, for the SARIF smoke check"

  model Described "Has everything the rules ask for"
    parameter Modelica.Units.SI.Length ell = 1 "Length of the thing";
  end Described;

  model Undescribed
    parameter Real gain = 1;
    Real state;
  end Undescribed;

  type Fraction = Real "An alias that fixes no unit";

  model UsesTheAlias "Uses a type that fixes no unit"
    Fraction f;
  end UsesTheAlias;

end Smoke;
