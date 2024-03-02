%% load Nexus client
connectorFolderPath = fullfile(tempdir, 'nexus');
[~, ~]              = mkdir(connectorFolderPath);
url                 = 'https://raw.githubusercontent.com/malstroem-labs/nexus/master/src/clients/matlab-client/NexusClient.m';
websave(fullfile(connectorFolderPath, 'NexusClient.m'), url);
addpath(connectorFolderPath)

%% Create client and authenticate
% - You get this token in the Nexus GUI's user menu. 
% - To avoid the token being invalidated by Nexus, do not use it in parallel.
% - Best practice: Create one token per script or one token per "thread".
refreshToken    = '<token>';
baseUrl         = 'http://localhost:5000';
client          = NexusClient(baseUrl);

client.signIn(refreshToken)

%% Export data from sample catalog /SAMPLE/LOCAL
dateTimeBegin 	= datetime(2020, 01, 01, 0, 0, 0, 'TimeZone', 'UTC');
dateTimeEnd 	= datetime(2020, 01, 02, 0, 0, 0, 'TimeZone', 'UTC');

T1              = '/SAMPLE/LOCAL/T1/1_s';
V1              = '/SAMPLE/LOCAL/V1/1_s';

resourcePaths   = { ...
    T1
    V1
};

% Use a file period of zero to write all data into a single file.
filePeriod = duration(0, 0, 0);

% The following writers are currently supported: 
% 'Nexus.Writers.Csv' | 'Nexus.Writers.Hdf5' | 'Nexus.Writers.Mat73' | 'Nexus.Writers.Famos'
fileFormat = 'Nexus.Writers.Csv';

% Nexus.Writers.Csv supports the following optional request configuration parameters:
% https://github.com/malstroem-labs/nexus/blob/master/src/Nexus/Extensions/Writers/README.md
configuration = containers.Map;
configuration('significant-figures') = '4';
configuration('row-index-format') = 'iso-8601';

client.export(...
    dateTimeBegin, ...
    dateTimeEnd, ...
    filePeriod, ...
    fileFormat, ...
    resourcePaths, ...
    configuration, ...
    'data', ...         % target folder 
    @(progress, message) fprintf('%3.0f %%: %s\n', progress * 100, message));