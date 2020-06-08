This simple Matlab script connects to an FX3 and reads the primary XL output registers from an ADIS1650x DUT. Each set of reads are data ready synchronized, and the resulting data is FFT'd and plotted.

Reference for .NET <-> Matlab data conversions can be found here: https://www.mathworks.com/help/matlab/matlab_external/passing-net-data-in-matlab.html

Note, any hardcoded resource paths will have to be adjusted to match your system
