using Dapper;
using OrderService.Application.Interfaces;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Database.Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderService.Infrastructure.Database.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly DapperContext _context;
        
        public OrderRepository(DapperContext context)
        {
            _context = context;
        }
        public async Task<int> CreateAsync(Order order)
        {
            var sql = @"
                INSERT INTO Orders (CustomerName, TotalAmount, Status, CreatedAt)
                VALUES (@CustomerName, @TotalAmount, @Status, @CreatedAt)
                RETURNING Id;";

            using var connection = _context.CreateConnection();
            var id = await connection.ExecuteScalarAsync<int>(sql, order);
            return id;

        }

        public async Task<Order?> GetByIdAsync(int id)
        {
            var sql = "SELECT * FROM Orders WHERE Id = @Id";
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<Order>(sql, new {Id = id});
        }
    }
}
