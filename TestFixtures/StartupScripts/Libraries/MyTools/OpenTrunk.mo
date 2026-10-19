within MyTools;
function OpenTrunk
  "Opens Trunk Libraries, with choices of which to open"

  //Location of appropriate version of DymolaCommands:
  //<company folder>\Modelica\Supporting Libraries\DymolaCommands 1.7

  input Boolean modelica=false
    "Open appropriate version of Modelica, for DymolaCommands see text"
    annotation (Dialog(group="Supporting Libraries"), choices(checkBox=true));
  input Boolean vehicleInterfaces=false
    "Open appropriate version of Vehicle Interfaces"
    annotation (Dialog(group="Supporting Libraries"), choices(checkBox=true));

  input Boolean all=false "Open all libraries" annotation (Dialog(group="Groups of Libraries",
        enable=not releases), choices(checkBox=
         true));

  input Boolean vehicleDynamics=true "Open vehicle dynamics related libraries"
    annotation (Dialog(group="Library Groups By Catagory", enable=not all and
          not releases), choices(checkBox=true));
  input Boolean powertrainDynamics=true "Open powertrain dynamics related libraries"
    annotation (Dialog(group="Library Groups By Catagory", enable=not all and
          not releases), choices(checkBox=true));

protected
  String path;

algorithm
  path := "{root}\Libraries";

  if modelica then
    DymolaCommands.SimulatorAPI.openModel(path + "\Modelica 3.2.3\package.mo",
      changeDirectory=false);
  end if;

  if vehicleInterfaces then
    DymolaCommands.SimulatorAPI.openModel(path + "\VehicleInterfaces 1.2.5\package.mo",
      changeDirectory=false);
  end if;

  if vehicleDynamics or all then
    DymolaCommands.SimulatorAPI.openModel(path + "\VehicleDyn\package.mo",
      changeDirectory=false);
  end if;

  if powertrainDynamics or all then
    MyTools.OpenPowertrain(path);
  end if;

end OpenTrunk;
