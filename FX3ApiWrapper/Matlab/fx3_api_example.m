clear;
%Load wrapper DLL
NET.addAssembly('C:\Users\anolan3\Documents\iSensor-FX3-API\FX3ApiWrapper\bin\Debug\FX3ApiWrapper.dll');
%Create FX3 wrapper
FX3 = FX3ApiWrapper.FX3Wrapper('C:\Users\anolan3\Documents\iSensor-FX3-API\Resources');
%Create DUT interface wrapper
Dut = FX3ApiWrapper.DutInterfaceWrapper(FX3,'C:\Users\anolan3\Documents\iSensor-FX3-ExampleGui\src\ADIS1650x_Regmap.csv');
data = [];
%Create reglist
regs = NET.createArray('System.String',8);
regs(1) = 'STATUS';
regs(2) = 'DATA_CNTR';
regs(3) = 'XGYRO_UPR';
regs(4) = 'YGYRO_UPR';
regs(5) = 'ZGYRO_UPR';
regs(6) = 'XACCL_UPR';
regs(7) = 'YACCL_UPR';
regs(8) = 'ZACCL_UPR';
while(true)
    data = int32(Dut.ReadUnsigned(regs));
    disp(data)
    pause(0.5)
end