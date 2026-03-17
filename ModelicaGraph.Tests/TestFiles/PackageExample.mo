package TestPackage
  model ModelA
    Real a;
  equation
    a = 1.0;
  end ModelA;

  model ModelB
    ModelA compA;
    Real b;
  equation
    b = 2.0;
  end ModelB;
end TestPackage;
