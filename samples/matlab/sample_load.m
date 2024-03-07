%% load Nexus client
connectorFolderPath = fullfile(tempdir, 'nexus');
[~, ~]              = mkdir(connectorFolderPath);
url                 = 'https://raw.githubusercontent.com/malstroem-labs/nexus/master/src/clients/matlab-client/NexusClient.m';
websave(fullfile(connectorFolderPath, 'NexusClient.m'), url);
addpath(connectorFolderPath)

%% Create client and authenticate
% You get this token in the user settings menu of Nexus.
accessToken     = '<token>';
baseUrl         = 'http://localhost:5000';
client          = NexusClient(baseUrl);

client.signIn(accessToken)

%% Load data from sample catalog /SAMPLE/LOCAL
dateTimeBegin 	= datetime(2020, 01, 01, 0, 0, 0, 'TimeZone', 'UTC');
dateTimeEnd 	= datetime(2020, 01, 01, 0, 2, 0, 'TimeZone', 'UTC');

T1              = '/SAMPLE/LOCAL/T1/1_s';
T1_MEAN         = '/SAMPLE/LOCAL/T1/5_s_mean';

resourcePaths   = { ...
    T1
    T1_MEAN
};

data            = client.load(...
                    dateTimeBegin, ...
                    dateTimeEnd, ...
                    resourcePaths, ...
                    @(progress) fprintf('%3.0f %%: Loading\n', progress * 100));

t1    	        = data(T1);
t1_mean         = data(T1_MEAN);

timestamps      = dateTimeBegin : t1.SamplePeriod : dateTimeEnd - t1.SamplePeriod;
timestamps_mean = dateTimeBegin : t1_mean.SamplePeriod : dateTimeEnd - t1_mean.SamplePeriod;

%% Plot data
yyaxis left
plot(timestamps, t1.Values, 'color', [0.0000, 0.4473, 0.7410]);
ylabel(sprintf('%s / %s', t1.Description, t1.Unit))
ylim([0 10])

yyaxis right
plot(timestamps_mean, t1_mean.Values, 'color', [0.8500, 0.3250, 0.0980])
ylabel(sprintf('%s (mean) / %s', t1.Description, t1.Unit))
ylim([0 10])

title('Nexus API Sample')
xlabel('Time')
xlim([timestamps(1) timestamps(end)])
grid on