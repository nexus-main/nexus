classdef NexusClient < handle

    properties(Access = private)
        BaseUrl
        AuthorizationHeader
        ConfigurationHeader
    end

    methods

        function self = NexusClient(baseUrl)
            self.BaseUrl = baseUrl; 
        end

        function signIn(self, accessToken)
            self.AuthorizationHeader = GenericField('Authorization', ['Bearer ' accessToken]);
        end

        function attachConfiguration(self, configuration)

            import matlab.net.*
            import matlab.net.http.field.*

            encodedJson = base64encode(jsonencode(configuration));
            self.ConfigurationHeader = GenericField('Nexus-Configuration', encodedJson);
        end

        function clearConfiguration(self)
            self.ConfigurationHeader = [];
        end

        function result = load(self, dateTimeBegin, dateTimeEnd, resourcePaths, onProgress)
            
            dateTimeBegin.TimeZone  = 'Z';
            dateTimeEnd.TimeZone    = 'Z';
            
            dateTimeBegin           = datestr(dateTimeBegin, 'yyyy-mm-ddTHH:MM:ssZ');
            dateTimeEnd             = datestr(dateTimeEnd, 'yyyy-mm-ddTHH:MM:ssZ');

            catalogItemMap          = self.catlogs_searchCatalogItems(resourcePaths);
            result                  = containers.Map;
            progress                = 0.0;

            for resourcePath = catalogItemMap.keys
                catalogItem = catalogItemMap(char(resourcePath));
                response    = self.data_getStream(char(resourcePath), dateTimeBegin, dateTimeEnd);
                doubleData  = typecast(response.Body.Data, 'double');

                resource    = catalogItem.Resource;
                unit        = [];
                description = [];

                if isfield(resource, 'Properties')
                    properties = resource.Properties;

                    if isfield(properties, 'Unit') && ischar(properties.Unit)
                        unit = properties.Unit;
                    end

                    if isfield(properties, 'Description') && ischar(properties.Description)
                        description = properties.Description;
                    end
                end

                samplePeriod = catalogItem.Representation.SamplePeriod;

                dataResponse = struct(...
                    'CatalogItem', catalogItem, ...
                    'Name', resource.Id, ...
                    'Unit', unit, ...
                    'Description', description, ...
                    'SamplePeriod', samplePeriod, ...
                    'Values', doubleData);

                result(char(resourcePath)) = dataResponse;
                progress = progress + 1.0 / numel(resourcePaths);

                if ~isempty(onProgress)
                    onProgress(progress)
                end
            end
        end

        function export(self, dateTimeBegin, dateTimeEnd, filePeriod, fileFormat, ...
                        resourcePaths, configuration, targetFolder, onProgress)

            dateTimeBegin.TimeZone          = 'Z';
            dateTimeEnd.TimeZone            = 'Z';
            
            dateTimeBegin                   = datestr(dateTimeBegin, 'yyyy-mm-ddTHH:MM:ssZ');
            dateTimeEnd                     = datestr(dateTimeEnd, 'yyyy-mm-ddTHH:MM:ssZ');

            filePeriod.Format               = 'hh:mm:ss.SSSSSSS';
            filePeriodString                = char(filePeriod);
            filePeriodTotalHours            = hours(filePeriod);
            filePeriodDays                  = idivide(filePeriodTotalHours, int32(24));
            filePeriodHours                 = floor(mod(hours(filePeriod), 24));

            serializedFilePeriod            = sprintf('%d.%02d:%s', filePeriodDays, filePeriodHours, filePeriodString(end-12:end));

            exportParameters                = {};
            exportParameters.begin          = dateTimeBegin;
            exportParameters.end            = dateTimeEnd;
            exportParameters.filePeriod     = serializedFilePeriod;
            exportParameters.type           = fileFormat;
            exportParameters.resourcePaths  = resourcePaths;
            exportParameters.configuration  = configuration;

            % Start job
            job = self.jobs_export(exportParameters);
            
            % Wait for job to finish
            artifactId = [];

            while true
                pause(1)

                jobStatus = self.jobs_getJobStatus(job.id);

                if jobStatus.status == "Canceled"
                    error('The job has been canceled.')

                elseif jobStatus.status == "Faulted"
                    error(['The job has faulted. Reason:' jobStatus.exceptionMessage])

                elseif jobStatus.status == "RanToCompletion"
                    if ischar(jobStatus.result)
                        artifactId = jobStatus.result;
                        break
                    end
                end

                if jobStatus.progress < 1 && ~isempty(onProgress)
                    onProgress(jobStatus.progress, 'export')
                end
            end

            if ~isempty(onProgress)
                onProgress(1, 'export')
            end

            if isempty(artifactId)
                error('The job result is invalid.')
            end

            if isempty(fileFormat)
                return
            end

            % Download zip file
            downloadUrl = self.artifacts_download(artifactId);
            tmpFilePath = tempname; 

            import matlab.net.http.*
            import matlab.net.http.field.*
                                      
            options = weboptions('HeaderFields', self.AuthorizationHeader);

            websave(tmpFilePath, downloadUrl, options);
            onProgress(1, 'download')

            % Extract file
            unzip(tmpFilePath, targetFolder);
            onProgress(1, 'extract')
        end
    end

    methods (Access=private)

        function catalogItemMap = catlogs_searchCatalogItems(self, resourcePaths)
            import matlab.net.*
            import matlab.net.http.*
            import matlab.net.http.io.*
        
            provider        = JSONProvider(resourcePaths);
            requestMessage  = RequestMessage('post', [], provider);
            uri             = URI([self.BaseUrl '/api/v1/catalogs/search-items']);
            response        = self.send(requestMessage, uri);
            data            = response.Body.Data;
            catalogItemMap  = containers.Map;
            
            for resourcePath = resourcePaths'
                catalogItem = data.(matlab.lang.makeValidName(char(resourcePath)));
                catalogItem = self.toPascalCase(catalogItem);
                catalogItem.Representation.SamplePeriod = self.parseSamplePeriod(catalogItem.Representation.SamplePeriod);
                catalogItemMap(char(resourcePath)) = catalogItem;
            end
        end

        function response = data_getStream(self, resourcePath, dateTimeBegin, dateTimeEnd)
            import matlab.net.*
            import matlab.net.http.*
        
            requestMessage  = RequestMessage('get');

            uri             = URI([self.BaseUrl '/api/v1/data' ...
                                '?resourcePath=' resourcePath ...
                                '&begin=' dateTimeBegin ...
                                '&end=' dateTimeEnd]);

            response        = self.send(requestMessage, uri);
        end

        function job = jobs_export(self, exportParameters)
            import matlab.net.*
            import matlab.net.http.*
        
            requestMessage  = RequestMessage('post', [], exportParameters);
            uri             = URI([self.BaseUrl '/api/v1/jobs/export']);
            response        = self.send(requestMessage, uri);
            job             = response.Body.Data;
        end

        function jobStatus = jobs_getJobStatus(self, jobId)
            import matlab.net.*
            import matlab.net.http.*
        
            requestMessage  = RequestMessage('get');
            uri             = URI([self.BaseUrl sprintf('/api/v1/jobs/%s/status', jobId)]);
            response        = self.send(requestMessage, uri);
            jobStatus       = response.Body.Data;
        end

        function downloadUrl = artifacts_download(self, artifactId)
            import matlab.net.*
            
            downloadUrl = URI([self.BaseUrl sprintf('/api/v1/artifacts/%s', artifactId)]);
        end

        function response = send(self, requestMessage, uri)
            import matlab.net.http.*
                                      
            % prepare request
            requestMessage.Header   = [requestMessage.Header self.AuthorizationHeader self.ConfigurationHeader];
            
            % send request
            response         	    = requestMessage.send(uri);
            
            % process response
            if ~self.isSuccessStatusCode(response)

                message = [char(response.StatusLine.ReasonPhrase) ' - ' char(response.Body.Data)];
                error('The HTTP request failed with status code %d. The response message is: %s', response.StatusCode, message)
            end
        end
        
        function result = isSuccessStatusCode(~, response)
            result = 200 <= response.StatusCode && response.StatusCode < 300;
        end

        function samplePeriod = parseSamplePeriod(~, input)
            % -> matlab does not support nested groups (we need non-capturing groups here around the actual matching group)
            patterns = { ...
                '^(?<hours>[0-9]{2}):(?<minutes>[0-9]{2}):(?<seconds>[0-9]{2})$'
                '^(?<hours>[0-9]{2}):(?<minutes>[0-9]{2}):(?<seconds>[0-9]{2})\.(?<subseconds>[0-9]{7})$'
                '^(?<days>[0-9]+)\.(?<hours>[0-9]{2}):(?<minutes>[0-9]{2}):(?<seconds>[0-9]{2})$'
                '^(?<days>[0-9]+)\.(?<hours>[0-9]{2}):(?<minutes>[0-9]{2}):(?<seconds>[0-9]{2})\.(?<subseconds>[0-9]{7})$'
            };

            for pattern = patterns'
                match = regexp(input, char(pattern), 'names');

                if ~isempty(match)
                    break
                end
            end

            if isempty(match)
                error('could not parse duration')
            end

            days        = 0;
            hours       = str2double(match.hours);
            minutes     = str2double(match.minutes);
            seconds     = str2double(match.seconds);
            subseconds  = 0;

            if isfield(match, 'days')
                days = str2double(match.days);
            end

            if isfield(match, 'subseconds')
                subseconds = str2double(match.subseconds);
            end

            samplePeriod = duration(hours + days * 24, minutes, seconds, subseconds / 1000 / 10);
        end

        function newStruct = toPascalCase(self, struct)
            for fieldName       = fieldnames(struct).'
                oldFieldName    = char(fieldName);
                newFieldName    = [upper(oldFieldName(1)) oldFieldName(2 : end)];

                if isstruct(struct.(oldFieldName))
                    newStruct.(newFieldName) = self.toPascalCase(struct.(oldFieldName));
                else
                    newStruct.(newFieldName) = struct.(oldFieldName);
                end         
            end
        end
    end
end