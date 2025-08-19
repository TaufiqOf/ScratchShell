using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScratchShell.Models
{
    public class Snippet
    {
        public string Id { get; set; } 
        public string Name { get; set; }
        public string Code { get; set; }

        public Snippet()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}
