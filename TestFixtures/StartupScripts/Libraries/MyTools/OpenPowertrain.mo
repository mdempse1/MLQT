within MyTools;
function OpenPowertrain
  "Opens the powertrain libraries found under a folder"

  input String folder "Folder holding the libraries";
  input Boolean withTests=false "Also open the powertrain tests";

algorithm
  DymolaCommands.SimulatorAPI.openModel(folder + "\EngineDyn\package.mo",
    changeDirectory=false);

  if withTests then
    DymolaCommands.SimulatorAPI.openModel(folder + "\EngineDynTests\package.mo",
      changeDirectory=false);
  end if;

end OpenPowertrain;
