using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace spring
{
    public class Class1
    {
        public void A()
        {
            var res =
                Enumerable.Range(1, 100)
                    .Select(i => i * 2)
                    .Select(i => i / 3)
                    .Sum();
        }
    }
}
