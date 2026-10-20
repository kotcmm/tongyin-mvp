using System.Data.Common;
using Dapper;
using Tongyin.Application.Ai;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Works;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Works;

/// <summary>混元任务表实测，确保 JobId 重启恢复与终态不会重复派发。</summary>
public sealed class SqlModelGenerationJobRepositoryTests
{
    /// <summary>JobId 一旦写入，重建仓储对象后继续轮询原 JobId，不能覆盖。</summary>
    [Fact]
    public async Task SubmittedJob_PersistsAndSurvivesNewRepositoryInstance()
    {
        using var db = new Data();
        db.InsertClaimed("gen-1");
        var repo = new SqlModelGenerationJobRepository(db.Connect);
        await repo.RecordSubmittedAsync("gen-1", "cloud-job-1", Provider("3.2", "https://gateway.example/v1/ai3d/submit"));
        var recovered = await new SqlModelGenerationJobRepository(db.Connect).GetNextSubmittedAsync();
        Assert.NotNull(recovered);
        Assert.Equal("cloud-job-1", recovered.ProviderJobId);
        Assert.Equal("dp1:encrypted-key", recovered.ApiKeyCipher);
        Assert.Equal("3.2", recovered.Model);
        Assert.Equal("hunyuan3d", recovered.Adapter);
        Assert.Equal("https://gateway.example/v1/ai3d/submit", recovered.Endpoint);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.RecordSubmittedAsync("gen-1", "cloud-job-2", Provider()));
    }

    /// <summary>提交状态不明确时不会返回待轮询任务，也不能被再次写成另一个提交。</summary>
    [Fact]
    public async Task UncertainSubmit_HasNoProviderIdAndNoAutomaticRetry()
    {
        using var db = new Data();
        db.InsertClaimed("gen-1");
        var repo = new SqlModelGenerationJobRepository(db.Connect);
        Assert.Null(await repo.GetNextSubmittedAsync());
        Assert.Equal("submitting", db.Status());
    }

    /// <summary>WAIT/RUN 可以更新，DONE 是终态，不再纳入轮询。</summary>
    [Fact]
    public async Task FinishedJob_IsNotPolledAgain()
    {
        using var db = new Data();
        db.InsertClaimed("gen-1");
        var repo = new SqlModelGenerationJobRepository(db.Connect);
        await repo.RecordSubmittedAsync("gen-1", "cloud-job", Provider());
        await repo.UpdateStatusAsync("gen-1", "RUN");
        Assert.Equal("RUN", db.Status());
        await repo.UpdateStatusAsync("gen-1", "DONE");
        Assert.Null(await repo.GetNextSubmittedAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UpdateStatusAsync("gen-1", "RUN"));
    }

    private static AiProviderSettings Provider(string model = "3.0",
        string endpoint = "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit") => new()
    {
        Id = "p1", Stage = "model", Adapter = "hunyuan3d", Endpoint = endpoint,
        Model = model, Enabled = true, Default = true, ApiKeyCipher = "dp1:encrypted-key"
    };

    private sealed class Data : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-model-job-" + Guid.NewGuid().ToString("N") + ".db");

        public Data()
        {
            using var c = Connect(); c.Open();
            c.Execute("""
                CREATE TABLE ty_model_jobs(
                  generation_id TEXT PRIMARY KEY, work_id TEXT NOT NULL, owner_id TEXT NOT NULL,
                  provider_id TEXT NOT NULL, prompt_version INTEGER NOT NULL, provider_job_id TEXT NULL,
                  api_key_cipher TEXT NULL, model TEXT NULL, adapter TEXT NULL, endpoint TEXT NULL,
                  status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
                );
                """);
        }

        public DbConnection Connect() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public void InsertClaimed(string id)
        {
            using var c = Connect(); c.Open();
            c.Execute("""
                INSERT INTO ty_model_jobs(generation_id,work_id,owner_id,provider_id,prompt_version,status,created_at,updated_at)
                VALUES(@Id,'work1','owner1','p1',1,'submitting','2026-10-19','2026-10-19')
                """, new { Id = id });
        }

        public string Status()
        {
            using var c = Connect(); c.Open();
            return c.QuerySingle<string>("SELECT status FROM ty_model_jobs");
        }

        public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }
    }
}
