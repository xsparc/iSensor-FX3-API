clear;
%Load wrapper DLL
NET.addAssembly('C:\Users\anolan3\Documents\iSensor-FX3-API\FX3ApiWrapper\bin\Debug\FX3ApiWrapper.dll');
%Create FX3 wrapper, with ADIS1650x regmap
Dut = FX3ApiWrapper.Wrapper('C:\Users\anolan3\Documents\iSensor-FX3-API\Resources',...
    'C:\Users\anolan3\Documents\iSensor-FX3-ExampleGui\src\ADIS1650x_Regmap.csv',...
    FX3ApiWrapper.SensorType.StandardImu);

%Blink user LED at 2Hz
Dut.UserLEDBlink(2.0);

%array to hold DUT data
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