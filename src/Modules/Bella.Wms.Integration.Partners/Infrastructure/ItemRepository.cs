using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Data;
using Dapper;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IItemRepository"/> over <c>PUB.item</c>.
/// </summary>
/// <remarks>Columns verified against <c>schema/irms.df</c>, 2026-09-04.</remarks>
public sealed class ItemRepository : IItemRepository
{
    private const string SelectSql = """
        SELECT abs_num   AS ItemNumber,
               item_type AS ItemType,
               case_qty  AS CaseQuantity
        FROM   PUB.item
        WHERE  co_num  = @company
        AND    wh_num  = @warehouse
        AND    abs_num = @itemNumber
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;

    public ItemRepository(IIrmsUnitOfWork unitOfWork) =>
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

    /// <inheritdoc />
    public async Task<Item?> FindAsync(
        string company,
        string warehouse,
        string itemNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemNumber))
        {
            // The ABL runs the FIND with a blank item and finds nothing. Callers rely on
            // that: locusAPI.cls:3495 passes an item number off an unpopulated buffer.
            return null;
        }

        return await _unitOfWork.Connection.QueryFirstOrDefaultAsync<Item>(
            new CommandDefinition(
                SelectSql,
                new { company, warehouse, itemNumber },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetCaseQuantityAsync(
        string company,
        string warehouse,
        string itemNumber,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(company, warehouse, itemNumber, cancellationToken)
            .ConfigureAwait(false);

        // 1, not 0, when the item is missing — static_connect.cls:2724.
        return item?.CaseQuantity ?? 1;
    }
}
