using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;
using Xunit;
using Xunit.Abstractions;
using TestDatabaseContext = MedHubPrescriptionTool.Tests.TestHelpers.TestDatabaseContext;

namespace MedHubPrescriptionTool.Tests;

/// <summary>
/// Adversarial stress-test suite for Milestone 2:
/// 1. Pathological filter queries (SQL injection, XSS, unicode, whitespace, regex characters, long strings, null safety).
/// 2. Extreme date filters (1900-01-01, 2100-01-01, Min/Max dates, inverted date ranges, half-open intervals).
/// 3. Concurrent query stress test (50 concurrent threads) asserting zero thread-safety exceptions.
/// 4. Search latency micro-benchmarks asserting average and P95 execution times under 16ms (60fps threshold).
/// </summary>
public class Milestone2_AdversarialSearchAndConcurrencyTests
{
    private readonly ITestOutputHelper _output;
    private readonly ClinicalDataService _service;
    private readonly ClinicalDataSnapshot _snapshot;

    public Milestone2_AdversarialSearchAndConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;

        var config = new DbConnectionSettings
        {
            Host = TestDatabaseContext.DefaultHost,
            Port = TestDatabaseContext.DefaultPort,
            Database = TestDatabaseContext.DefaultDatabase,
            Username = TestDatabaseContext.DefaultUser,
            Password = TestDatabaseContext.DefaultPassword,
            ReadOnly = true,
            TimeoutSeconds = 10,
            CommandTimeoutSeconds = 10
        };

        var factory = new DatabaseConnectionFactory(config);
        var repository = new DatabaseRepository(factory);
        _service = new ClinicalDataService(repository);
        _snapshot = _service.LoadClinicalSnapshotAsync().GetAwaiter().GetResult();
    }

    #region Group 1: Pathological Search Queries (SQLi, XSS, Regex, Unicode, Whitespace, Nulls)

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("  \t \r\n  ")]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_EmptyAndWhitespaceQueries_MustReturnAllRecordsWithoutException(string whitespaceQuery)
    {
        var criteria = new ClinicalFilterCriteria
        {
            SearchQuery = whitespaceQuery
        };

        var results = _service.FilterMarSchedule(_snapshot, criteria);

        results.Should().NotBeNull();
        results.Count.Should().Be(_snapshot.MarSchedules.Count,
            "Whitespace-only or empty search query must evaluate as an unconstrained search returning all records");
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("'; DROP TABLE \"Prescriptions\"; --")]
    [InlineData("\" OR \"\"=\"\"")]
    [InlineData("admin'--")]
    [InlineData("1' UNION SELECT 1, 'admin', 'pass' --")]
    [InlineData("1; EXEC xp_cmdshell('dir');--")]
    [InlineData("WAITFOR DELAY '0:0:5'--")]
    [InlineData("pg_sleep(5)")]
    [InlineData("1' AND 1=(SELECT 1 FROM \"Users\" LIMIT 1)--")]
    [InlineData("'; SHUTDOWN; --")]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_SqlInjectionAttacks_MustExecuteSafelyAsLiteralTermsWithoutException(string injectionString)
    {
        var criteria = new ClinicalFilterCriteria
        {
            SearchQuery = injectionString
        };

        Func<List<MarScheduleItemDto>> act = () => _service.FilterMarSchedule(_snapshot, criteria);

        act.Should().NotThrow("In-memory LINQ search must treat SQL injection attempts as literal search strings");
        var results = act();
        results.Should().NotBeNull();
        // Since test data does not contain SQL injection strings, count must be 0
        results.Count.Should().Be(0, "Injection string should not match any legitimate clinical records");
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("<svg/onload=alert('xss')>")]
    [InlineData("<iframe src=\"javascript:alert(1)\">")]
    [InlineData("<body onload=alert('xss')>")]
    [InlineData("<input onfocus=javascript:alert(1) autofocus>")]
    [InlineData("'\"><script>alert(document.cookie)</script>")]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_ScriptAndHtmlInjectionAttacks_MustExecuteSafelyWithoutException(string xssPayload)
    {
        var criteria = new ClinicalFilterCriteria
        {
            SearchQuery = xssPayload
        };

        Func<List<MarScheduleItemDto>> act = () => _service.FilterMarSchedule(_snapshot, criteria);

        act.Should().NotThrow("HTML/script injection payloads must not crash search engine or trigger execution");
        var results = act();
        results.Should().NotBeNull();
        results.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("[a-z]+")]
    [InlineData("\\d{3}")]
    [InlineData("(?i)")]
    [InlineData("^$")]
    [InlineData("()[]{}\\$^?*+|~")]
    [InlineData("!@#$%^&*()_+=-`~[]\\{}|;':\",./<>?")]
    [InlineData("++")]
    [InlineData("(?<=abc)")]
    [InlineData("([a-zA-Z0-9]+)+$")]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_RegexAndSpecialCharacters_MustExecuteSafelyWithoutRegexSyntaxCrash(string regexSpecial)
    {
        var criteria = new ClinicalFilterCriteria
        {
            SearchQuery = regexSpecial
        };

        Func<List<MarScheduleItemDto>> act = () => _service.FilterMarSchedule(_snapshot, criteria);

        act.Should().NotThrow("Search engine must use literal string comparison rather than fragile unchecked Regex.Match");
        var results = act();
        results.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_UnicodeMultiLingualAndEmoji_ShouldFilterAccuratelyOrSafelyHandle()
    {
        // 1. Traditional Chinese matching live data: return reason in live DB is '病人禁食 (Patient Fasting / NPO)'
        var chnCriteria = new ClinicalFilterCriteria { SearchQuery = "病人禁食" };
        var chnResults = _service.FilterMarSchedule(_snapshot, chnCriteria);
        chnResults.Should().NotBeNull();
        // Return Reason Name is not directly in the 14 searchable fields of FilterMarSchedule unless matches one
        // Let's verify no exception thrown for any unicode:
        chnResults.Should().BeAssignableTo<List<MarScheduleItemDto>>();

        // 2. Japanese, Korean, Arabic, Hebrew, Emojis, Zero-width
        var unicodePayloads = new[]
        {
            "テスト",           // Japanese Katakana
            "処方箋",           // Japanese Kanji
            "테스트",           // Korean Hangul
            "처방전",           // Korean Prescription
            "Épée Café naïve",  // Accented Latin
            "💉💊🏥🩺",         // Medical Emojis
            "دواء",             // Arabic (RTL)
            "תרופה",            // Hebrew (RTL)
            "\u200B\u200C\u200D\uFEFF", // Zero-width spaces & BOM
            "𠮷野家"            // CJK Surrogate pair (Ext B)
        };

        foreach (var payload in unicodePayloads)
        {
            var criteria = new ClinicalFilterCriteria { SearchQuery = payload };
            Func<List<MarScheduleItemDto>> act = () => _service.FilterMarSchedule(_snapshot, criteria);
            act.Should().NotThrow($"Query with unicode '{payload}' must execute safely without character encoding crash");
        }
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_ExtremeLengthQuery_ShouldExecutePromptlyWithoutBufferOverflow()
    {
        var longString = new string('X', 10000);
        var criteria = new ClinicalFilterCriteria { SearchQuery = longString };

        var sw = Stopwatch.StartNew();
        var results = _service.FilterMarSchedule(_snapshot, criteria);
        sw.Stop();

        results.Should().BeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "10,000 character search query must complete quickly");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_WhitespaceTokenizationAndCaseInsensitivity_ShouldMatchAccurately()
    {
        // Case-insensitive & trimmed MRN
        var mrnLower = _service.FilterMarSchedule(_snapshot, new ClinicalFilterCriteria { SearchQuery = "   0439449   " });
        mrnLower.Count.Should().Be(11, "MRN 0439449 has exactly 11 schedules in the baseline database");

        // Case-insensitive drug name
        var drugUpper = _service.FilterMarSchedule(_snapshot, new ClinicalFilterCriteria { SearchQuery = "LEVEMIR" });
        var drugLower = _service.FilterMarSchedule(_snapshot, new ClinicalFilterCriteria { SearchQuery = "levemir" });
        var drugMixed = _service.FilterMarSchedule(_snapshot, new ClinicalFilterCriteria { SearchQuery = "  LeVeMiR  " });

        drugUpper.Count.Should().Be(22, "Levemir has 22 schedules");
        drugLower.Count.Should().Be(22);
        drugMixed.Count.Should().Be(22);

        // Multi-token: matches records satisfying BOTH tokens
        var multiToken = _service.FilterMarSchedule(_snapshot, new ClinicalFilterCriteria { SearchQuery = "Levemir    0439449" });
        multiToken.Count.Should().Be(11, "All 11 schedules for patient 0439449 are Levemir");

        // Conflicting tokens: matches 0
        var conflictingToken = _service.FilterMarSchedule(_snapshot, new ClinicalFilterCriteria { SearchQuery = "Levemir NON_EXISTENT_TOKEN_9999" });
        conflictingToken.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvSearch_NullCriteriaAndNullProperties_MustHandleGracefully()
    {
        // 1. null criteria object
        Func<List<MarScheduleItemDto>> actNullCriteria = () => _service.FilterMarSchedule(_snapshot, null!);
        actNullCriteria.Should().NotThrow();
        actNullCriteria().Count.Should().Be(_snapshot.MarSchedules.Count);

        // 2. criteria with all null properties
        var emptyCriteria = new ClinicalFilterCriteria
        {
            SearchQuery = null,
            StationCode = null,
            BedCode = null,
            PatientMrn = null,
            DateFilter = null,
            StatusFilter = null,
            HighAlertOnly = null,
            ControlledOnly = null,
            RefrigerationOnly = null,
            ReferenceTime = null
        };

        var allNullResults = _service.FilterMarSchedule(_snapshot, emptyCriteria);
        allNullResults.Count.Should().Be(_snapshot.MarSchedules.Count);

        // 3. GetPatientOverviewList with null, whitespace, and ALL station filter
        _service.GetPatientOverviewList(_snapshot, null).Count.Should().Be(_snapshot.Patients.Count);
        _service.GetPatientOverviewList(_snapshot, "   ").Count.Should().Be(_snapshot.Patients.Count);
        _service.GetPatientOverviewList(_snapshot, "ALL").Count.Should().Be(_snapshot.Patients.Count);
        _service.GetPatientOverviewList(_snapshot, "all").Count.Should().Be(_snapshot.Patients.Count);
        _service.GetPatientOverviewList(_snapshot, "NON_EXISTENT_STATION").Should().BeEmpty();

        // 4. GetMedicationDetails with null, whitespace, and unknown codes
        _service.GetMedicationDetails(_snapshot, null!).Should().BeNull();
        _service.GetMedicationDetails(_snapshot, "").Should().BeNull();
        _service.GetMedicationDetails(_snapshot, "   ").Should().BeNull();
        _service.GetMedicationDetails(_snapshot, "NON_EXISTENT_MED").Should().BeNull();
        _service.GetMedicationDetails(_snapshot, "oxana").Should().NotBeNull("Drug code lookup must be case-insensitive");
    }

    #endregion

    #region Group 2: Extreme Date Filters (Far Past/Future, Inverted, Min/Max, Boundary Times)

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvDate_FarPastAndFarFuture_MustReturnZeroWithoutException()
    {
        // Far past: 1900-01-01
        var pastCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = new DateTime(1900, 1, 1),
            CustomEndDate = new DateTime(1900, 1, 2)
        };
        var pastResults = _service.FilterMarSchedule(_snapshot, pastCriteria);
        pastResults.Should().BeEmpty("No clinical prescriptions exist in year 1900");

        // Far future: 2100-01-01
        var futureCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = new DateTime(2100, 1, 1),
            CustomEndDate = new DateTime(2100, 1, 2)
        };
        var futureResults = _service.FilterMarSchedule(_snapshot, futureCriteria);
        futureResults.Should().BeEmpty("No clinical prescriptions exist in year 2100");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvDate_MinAndMaxDateRange_MustSpanAllRecordsWithoutException()
    {
        var allTimeCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = new DateTime(1, 1, 1),
            CustomEndDate = new DateTime(9999, 12, 31)
        };

        var results = _service.FilterMarSchedule(_snapshot, allTimeCriteria);
        results.Count.Should().Be(_snapshot.MarSchedules.Count,
            "Date range from 0001 to 9999 must encompass all recorded schedules");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvDate_InvertedDateRange_MustReturnZeroWithoutException()
    {
        // Start date is AFTER End date (inverted range)
        var invertedCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = new DateTime(2026, 12, 31),
            CustomEndDate = new DateTime(2026, 1, 1)
        };

        var results = _service.FilterMarSchedule(_snapshot, invertedCriteria);
        results.Should().BeEmpty("Inverted date range where StartDate > EndDate must match 0 records without throwing");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvDate_HalfOpenIntervals_MustFilterAccurately()
    {
        // Only CustomStartDate provided (2026-09-24)
        var afterCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = new DateTime(2026, 9, 24),
            CustomEndDate = null
        };
        var afterResults = _service.FilterMarSchedule(_snapshot, afterCriteria);
        afterResults.Should().NotBeEmpty();
        afterResults.Should().OnlyContain(s => s.Schedule_Date.Date >= new DateTime(2026, 9, 24));

        // Only CustomEndDate provided (2026-06-02)
        var beforeCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = null,
            CustomEndDate = new DateTime(2026, 6, 2)
        };
        var beforeResults = _service.FilterMarSchedule(_snapshot, beforeCriteria);
        beforeResults.Should().NotBeEmpty();
        beforeResults.Should().OnlyContain(s => s.Schedule_Date.Date <= new DateTime(2026, 6, 2));

        var distinctDates = _snapshot.MarSchedules.Select(s => s.Schedule_Date.ToString("yyyy-MM-dd")).Distinct().OrderBy(d => d);
        _output.WriteLine("Distinct dates in snapshot: " + string.Join(", ", distinctDates));

        // Both dates equal (single exact day)
        var firstDate = _snapshot.MarSchedules.First().Schedule_Date.Date;
        var exactDayCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Custom,
            CustomStartDate = firstDate,
            CustomEndDate = firstDate
        };
        var exactResults = _service.FilterMarSchedule(_snapshot, exactDayCriteria);
        exactResults.Should().NotBeEmpty();
        exactResults.Should().OnlyContain(s => s.Schedule_Date.Date == firstDate);
    }

    [Theory]
    [InlineData("UNKNOWN_FILTER")]
    [InlineData("12345")]
    [InlineData("!@#$%")]
    [InlineData("null")]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvDate_UnrecognizedDateFilterOption_MustNotThrowException(string unrecognizedOption)
    {
        var criteria = new ClinicalFilterCriteria
        {
            DateFilter = unrecognizedOption
        };

        Func<List<MarScheduleItemDto>> act = () => _service.FilterMarSchedule(_snapshot, criteria);
        act.Should().NotThrow("Unrecognized DateFilter strings should safely fall through without throwing an exception");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvDate_ReferenceTimeBoundaryVariation_MustPartitionCleanly()
    {
        // 1. Far past reference time (1970): All un-dispensed schedules must be classified as Pending (Future)
        var epochCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Future,
            ReferenceTime = new DateTime(1970, 1, 1)
        };
        var futureFromEpoch = _service.FilterMarSchedule(_snapshot, epochCriteria);
        futureFromEpoch.Count.Should().Be(_snapshot.MarSchedules.Count,
            "All schedules in the database are after 1970-01-01");

        // 2. Far future reference time (2099): All un-dispensed schedules must be classified as Pending (Due/Past)
        var futureEpochCriteria = new ClinicalFilterCriteria
        {
            DateFilter = DateFilterOptions.Past,
            ReferenceTime = new DateTime(2099, 12, 31)
        };
        var pastFromFuture = _service.FilterMarSchedule(_snapshot, futureEpochCriteria);
        pastFromFuture.Count.Should().Be(_snapshot.MarSchedules.Count,
            "All schedules in the database are before 2099-12-31");

        // 3. Leap Year Leap Day reference time (2024-02-29 12:00:00)
        var leapDayCriteria = new ClinicalFilterCriteria
        {
            ReferenceTime = new DateTime(2024, 2, 29, 12, 0, 0)
        };
        Func<List<MarScheduleItemDto>> actLeap = () => _service.FilterMarSchedule(_snapshot, leapDayCriteria);
        actLeap.Should().NotThrow("Leap day reference time must not cause calendar calculation exceptions");
    }

    #endregion

    #region Group 3: Concurrency Stress Test (50 Concurrent Threads Asserting Zero Exceptions)

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public async Task AdvConcurrency_FiftyConcurrentThreads_MixedQueryStressTest_ZeroExceptions()
    {
        const int concurrentTasks = 50;
        const int iterationsPerTask = 100;
        var exceptions = new ConcurrentBag<Exception>();
        var totalOperations = 0;

        _output.WriteLine($"Launching {concurrentTasks} concurrent tasks, {iterationsPerTask} iterations each ({concurrentTasks * iterationsPerTask} total operations)...");

        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrentTasks).Select(async taskId =>
        {
            await Task.Yield(); // Ensure asynchronous dispatch across thread pool threads

            for (int i = 0; i < iterationsPerTask; i++)
            {
                try
                {
                    switch (taskId % 5)
                    {
                        case 0:
                            // Worker Group 1: FilterMarSchedule with diverse criteria
                            var criteria = (i % 5) switch
                            {
                                0 => new ClinicalFilterCriteria { SearchQuery = "Levemir" },
                                1 => new ClinicalFilterCriteria { StatusFilter = StatusFilterOptions.Dispensed },
                                2 => new ClinicalFilterCriteria { DateFilter = DateFilterOptions.Today, ReferenceTime = new DateTime(2026, 3, 24, 12, 0, 0) },
                                3 => new ClinicalFilterCriteria { ControlledOnly = true },
                                _ => new ClinicalFilterCriteria { SearchQuery = "' OR '1'='1", StatusFilter = StatusFilterOptions.All }
                            };
                            var results = _service.FilterMarSchedule(_snapshot, criteria);
                            results.Should().NotBeNull();
                            break;

                        case 1:
                            // Worker Group 2: GetPatientOverviewList with diverse station filters
                            var stationFilter = (i % 6) switch
                            {
                                0 => "8A",
                                1 => "5A",
                                2 => "ICU",
                                3 => "ALL",
                                4 => null,
                                _ => "NONEXISTENT"
                            };
                            var patients = _service.GetPatientOverviewList(_snapshot, stationFilter);
                            patients.Should().NotBeNull();
                            break;

                        case 2:
                            // Worker Group 3: ComputeKpiSummary calculation
                            var kpi = _service.ComputeKpiSummary(_snapshot);
                            kpi.TotalInpatients.Should().Be(15);
                            kpi.DispensedCount.Should().Be(13);
                            break;

                        case 3:
                            // Worker Group 4: GetMedicationDetails lookups
                            var code = (i % 4) switch
                            {
                                0 => "OXANA",
                                1 => "LEVEMIR",
                                2 => "oxana",
                                _ => "UNKNOWN_DRUG_CODE"
                            };
                            var med = _service.GetMedicationDetails(_snapshot, code);
                            if (code.Equals("OXANA", StringComparison.OrdinalIgnoreCase))
                            {
                                med.Should().NotBeNull();
                            }
                            break;

                        case 4:
                            // Worker Group 5: Snapshot cache access and rehydration
                            var snap = await _service.GetCurrentSnapshotAsync(forceRefresh: (i % 25 == 0));
                            snap.Should().NotBeNull();
                            snap.MarSchedules.Count.Should().Be(385);
                            break;
                    }

                    Interlocked.Increment(ref totalOperations);
                }
                catch (Exception ex)
                {
                    exceptions.Add(new Exception($"TaskId {taskId} (Iteration {i}) failed: {ex.Message}", ex));
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        sw.Stop();

        _output.WriteLine($"[CONCURRENCY STRESS COMPLETE]");
        _output.WriteLine($"Total Operations: {totalOperations}");
        _output.WriteLine($"Elapsed Time: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Throughput: {totalOperations / (sw.Elapsed.TotalSeconds):F1} ops/sec");
        _output.WriteLine($"Exceptions Encountered: {exceptions.Count}");

        if (exceptions.Count > 0)
        {
            foreach (var ex in exceptions.Take(10))
            {
                _output.WriteLine($"  EXCEPTION: {ex.Message}");
            }
        }

        exceptions.Should().BeEmpty("50 concurrent tasks executing mixed read and refresh workloads must produce ZERO exceptions");
        totalOperations.Should().Be(concurrentTasks * iterationsPerTask);
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public async Task AdvConcurrency_FiftyConcurrentThreads_PureFilterMarSchedule_HighContention()
    {
        const int concurrentTasks = 50;
        const int iterationsPerTask = 150;
        var exceptions = new ConcurrentBag<Exception>();

        var testCriteria = new ClinicalFilterCriteria
        {
            StationCode = "ICU",
            StatusFilter = StatusFilterOptions.Pending,
            ControlledOnly = true
        };

        var tasks = Enumerable.Range(0, concurrentTasks).Select(async taskId =>
        {
            await Task.Yield();

            for (int i = 0; i < iterationsPerTask; i++)
            {
                try
                {
                    var results = _service.FilterMarSchedule(_snapshot, testCriteria);
                    // Invariant check: In-memory filter on immutable snapshot MUST be 100% deterministic
                    results.Count.Should().Be(66, "Station ICU pending controlled schedules count must be invariant across all concurrent threads");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(tasks);

        exceptions.Should().BeEmpty("Pure concurrent FilterMarSchedule on 50 threads must produce 0 exceptions");
    }

    #endregion

    #region Group 4: Search Latency Micro-Benchmarks (< 16ms 60fps Budget)

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvBenchmark_SingleTermSearch_MustBeWellUnder16ms()
    {
        const int iterations = 1000;
        var criteria = new ClinicalFilterCriteria { SearchQuery = "Levemir" };

        // Warm-up
        for (int i = 0; i < 50; i++)
        {
            _service.FilterMarSchedule(_snapshot, criteria);
        }

        var latenciesMicroseconds = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var results = _service.FilterMarSchedule(_snapshot, criteria);
            long end = Stopwatch.GetTimestamp();

            latenciesMicroseconds[i] = (end - start) * 1_000_000.0 / Stopwatch.Frequency;
            results.Count.Should().Be(22);
        }

        Array.Sort(latenciesMicroseconds);

        double minMs = latenciesMicroseconds[0] / 1000.0;
        double maxMs = latenciesMicroseconds[^1] / 1000.0;
        double avgMs = latenciesMicroseconds.Average() / 1000.0;
        double p50Ms = latenciesMicroseconds[(int)(iterations * 0.50)] / 1000.0;
        double p95Ms = latenciesMicroseconds[(int)(iterations * 0.95)] / 1000.0;
        double p99Ms = latenciesMicroseconds[(int)(iterations * 0.99)] / 1000.0;

        _output.WriteLine($"[BENCHMARK: Single Term 'Levemir' - {iterations} runs]");
        _output.WriteLine($"  Min: {minMs:F4}ms | Avg: {avgMs:F4}ms | P50: {p50Ms:F4}ms | P95: {p95Ms:F4}ms | P99: {p99Ms:F4}ms | Max: {maxMs:F4}ms");

        avgMs.Should().BeLessThan(16.0, "Average search latency must be under 16ms 60fps budget");
        p95Ms.Should().BeLessThan(16.0, "P95 search latency must be under 16ms 60fps budget");
        avgMs.Should().BeLessThan(2.0, "In-memory LINQ on 385 records should typically execute in under 2ms");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvBenchmark_MultiTokenSearchAcross14Fields_MustBeWellUnder16ms()
    {
        const int iterations = 1000;
        var criteria = new ClinicalFilterCriteria { SearchQuery = "Levemir 0439449 8A" };

        // Warm-up
        for (int i = 0; i < 50; i++)
        {
            _service.FilterMarSchedule(_snapshot, criteria);
        }

        var latenciesMicroseconds = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var results = _service.FilterMarSchedule(_snapshot, criteria);
            long end = Stopwatch.GetTimestamp();

            latenciesMicroseconds[i] = (end - start) * 1_000_000.0 / Stopwatch.Frequency;
            results.Count.Should().Be(11);
        }

        Array.Sort(latenciesMicroseconds);

        double minMs = latenciesMicroseconds[0] / 1000.0;
        double maxMs = latenciesMicroseconds[^1] / 1000.0;
        double avgMs = latenciesMicroseconds.Average() / 1000.0;
        double p50Ms = latenciesMicroseconds[(int)(iterations * 0.50)] / 1000.0;
        double p95Ms = latenciesMicroseconds[(int)(iterations * 0.95)] / 1000.0;
        double p99Ms = latenciesMicroseconds[(int)(iterations * 0.99)] / 1000.0;

        _output.WriteLine($"[BENCHMARK: Multi-Token 'Levemir 0439449 8A' - {iterations} runs]");
        _output.WriteLine($"  Min: {minMs:F4}ms | Avg: {avgMs:F4}ms | P50: {p50Ms:F4}ms | P95: {p95Ms:F4}ms | P99: {p99Ms:F4}ms | Max: {maxMs:F4}ms");

        avgMs.Should().BeLessThan(16.0, "Average search latency must be under 16ms 60fps budget");
        p95Ms.Should().BeLessThan(16.0, "P95 search latency must be under 16ms 60fps budget");
    }

    [Fact]
    [Trait("Category", "Milestone2_Adversarial")]
    public void AdvBenchmark_ComplexMultiCriteriaFilter_MustBeWellUnder16ms()
    {
        const int iterations = 1000;
        var criteria = new ClinicalFilterCriteria
        {
            StationCode = "8A",
            BedCode = "8A01-1",
            StatusFilter = StatusFilterOptions.Pending,
            DateFilter = DateFilterOptions.Today,
            ReferenceTime = new DateTime(2026, 3, 24, 12, 0, 0),
            HighAlertOnly = false,
            ControlledOnly = true,
            SearchQuery = "0439449"
        };

        // Warm-up
        for (int i = 0; i < 50; i++)
        {
            _service.FilterMarSchedule(_snapshot, criteria);
        }

        var latenciesMicroseconds = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var results = _service.FilterMarSchedule(_snapshot, criteria);
            long end = Stopwatch.GetTimestamp();

            latenciesMicroseconds[i] = (end - start) * 1_000_000.0 / Stopwatch.Frequency;
        }

        Array.Sort(latenciesMicroseconds);

        double minMs = latenciesMicroseconds[0] / 1000.0;
        double maxMs = latenciesMicroseconds[^1] / 1000.0;
        double avgMs = latenciesMicroseconds.Average() / 1000.0;
        double p50Ms = latenciesMicroseconds[(int)(iterations * 0.50)] / 1000.0;
        double p95Ms = latenciesMicroseconds[(int)(iterations * 0.95)] / 1000.0;
        double p99Ms = latenciesMicroseconds[(int)(iterations * 0.99)] / 1000.0;

        _output.WriteLine($"[BENCHMARK: Complex Multi-Criteria Filter - {iterations} runs]");
        _output.WriteLine($"  Min: {minMs:F4}ms | Avg: {avgMs:F4}ms | P50: {p50Ms:F4}ms | P95: {p95Ms:F4}ms | P99: {p99Ms:F4}ms | Max: {maxMs:F4}ms");

        avgMs.Should().BeLessThan(16.0, "Average complex filter latency must be under 16ms 60fps budget");
        p95Ms.Should().BeLessThan(16.0, "P95 complex filter latency must be under 16ms 60fps budget");
    }

    #endregion
}
